using System.Diagnostics;
using Microsoft.Data.Sqlite;
using RssReader.Application;

namespace RssReader.Infrastructure;

public sealed class SqliteLibraryBackupService(string databasePath) : ILibraryBackupService
{
    private readonly string _databasePath = Path.GetFullPath(databasePath);
    private string PendingRestorePath => $"{_databasePath}.restore-pending";

    public long GetLocalStorageBytes() =>
        GetFileSize(_databasePath) + GetFileSize($"{_databasePath}-wal");

    public async Task CreateBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var destinationPath = Path.GetFullPath(backupPath);
        EnsureDifferentPath(destinationPath);
        await ValidateBackupAsync(_databasePath, cancellationToken);
        await CreateSnapshotAsync(_databasePath, destinationPath, cancellationToken);
    }

    public async Task StageRestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(backupPath);
        EnsureDifferentPath(sourcePath);
        await ValidateBackupAsync(sourcePath, cancellationToken);

        var temporaryPath = $"{PendingRestorePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await CreateSnapshotAsync(sourcePath, temporaryPath, cancellationToken);
            await ValidateBackupAsync(temporaryPath, cancellationToken);
            await InitializeDatabaseAsync(temporaryPath, cancellationToken);
            await ValidateBackupAsync(temporaryPath, cancellationToken);
            SqliteConnection.ClearAllPools();
            DeleteDatabaseSidecars(PendingRestorePath);
            File.Move(temporaryPath, PendingRestorePath, overwrite: true);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(temporaryPath);
            TryDelete($"{temporaryPath}-wal");
            TryDelete($"{temporaryPath}-shm");
        }
    }

    public async Task<LibraryRestoreResult?> ApplyPendingRestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PendingRestorePath))
        {
            return null;
        }

        string? safetyBackupPath = null;
        try
        {
            await ValidateBackupAsync(PendingRestorePath, cancellationToken);
            if (File.Exists(_databasePath) && GetFileSize(_databasePath) > 0)
            {
                var directory = Path.GetDirectoryName(_databasePath)
                    ?? throw new InvalidOperationException("The local database directory is unavailable.");
                safetyBackupPath = Path.Combine(
                    directory,
                    $"rssreader-before-restore-{DateTimeOffset.UtcNow:yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}.db");
                await CreateBackupAsync(safetyBackupPath, cancellationToken);
                await CheckpointDatabaseAsync(cancellationToken);
            }

            SqliteConnection.ClearAllPools();
            DeleteDatabaseSidecars(_databasePath);
            File.Move(PendingRestorePath, _databasePath, overwrite: true);
            return new LibraryRestoreResult(true, safetyBackupPath, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SqliteException or InvalidDataException)
        {
            Trace.TraceError($"The staged library restore could not be applied: {exception}");
            TryDelete(PendingRestorePath);
            TryDelete($"{PendingRestorePath}-wal");
            TryDelete($"{PendingRestorePath}-shm");
            TryDelete($"{PendingRestorePath}-journal");
            return new LibraryRestoreResult(false, safetyBackupPath, exception.Message);
        }
    }

    private async Task CheckpointDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken) && reader.GetInt32(0) != 0)
        {
            throw new IOException("The local database could not be checkpointed before restore.");
        }
    }

    private static async Task InitializeDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        await new SqliteProfileStore(path).InitializeAsync(cancellationToken);
        await new SqliteCatalogStore(path).InitializeAsync(cancellationToken);
        await new SqliteReaderStore(path).InitializeAsync(cancellationToken);
    }

    private async Task CreateSnapshotAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The backup must have a destination directory.", nameof(destinationPath));
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var sourceConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = sourcePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            var destinationConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Pooling = false
            }.ToString();

            await Task.Run(() =>
            {
                using var source = new SqliteConnection(sourceConnectionString);
                source.Open();
                using var destination = new SqliteConnection(destinationConnectionString);
                destination.Open();
                source.BackupDatabase(destination);
            }, cancellationToken);

            await ValidateIntegrityAsync(temporaryPath, cancellationToken);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(temporaryPath);
            TryDelete($"{temporaryPath}-wal");
            TryDelete($"{temporaryPath}-shm");
        }
    }

    private static async Task ValidateBackupAsync(string path, CancellationToken cancellationToken)
    {
        await ValidateIntegrityAsync(path, cancellationToken);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('Profiles', 'CatalogFeeds', 'Articles');
            """;
        var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (tableCount != 3)
        {
            throw new InvalidDataException("The selected file is not a valid RSS Reader library backup.");
        }
    }

    private static async Task ValidateIntegrityAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The database file does not exist.", path);
        }

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA quick_check;";
            var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The database backup failed its integrity check.");
            }
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("The selected file is not a readable SQLite database.", exception);
        }
    }

    private void EnsureDifferentPath(string path)
    {
        if (string.Equals(path, _databasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The active library database cannot be used as its own backup.", nameof(path));
        }
    }

    private static long GetFileSize(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void DeleteDatabaseSidecars(string path)
    {
        File.Delete($"{path}-wal");
        File.Delete($"{path}-shm");
        File.Delete($"{path}-journal");
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException exception)
        {
            Trace.TraceWarning($"Could not remove temporary database file '{path}': {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceWarning($"Could not remove temporary database file '{path}': {exception.Message}");
        }
    }
}
