using Microsoft.Data.Sqlite;
using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Infrastructure;

public sealed class SqliteProfileStore(string databasePath) : IProfileStore
{
    private readonly string _connectionString = new SqliteConnectionStringBuilder
    {
        DataSource = databasePath
    }.ToString();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Profiles (
                Id TEXT NOT NULL PRIMARY KEY,
                Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                IsCatalogMaster INTEGER NOT NULL CHECK (IsCatalogMaster IN (0, 1)),
                PasswordHash TEXT NULL,
                RecoveryEmail TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Profiles_CatalogMaster
                ON Profiles(IsCatalogMaster) WHERE IsCatalogMaster = 1;
            INSERT INTO Profiles (Id, Name, IsCatalogMaster, PasswordHash, RecoveryEmail)
            VALUES ($id, $name, 1, NULL, NULL)
            ON CONFLICT(Id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", Profile.CreateCatalogMaster().Id);
        command.Parameters.AddWithValue("$name", ProfileNameValidator.CatalogMasterName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, IsCatalogMaster, PasswordHash, RecoveryEmail
            FROM Profiles
            ORDER BY IsCatalogMaster, Name COLLATE NOCASE;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var profiles = new List<Profile>();
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(ReadProfile(reader));
        }

        return profiles;
    }

    public async Task<Profile?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, IsCatalogMaster, PasswordHash, RecoveryEmail
            FROM Profiles
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProfile(reader) : null;
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM Profiles WHERE Name = $name);";
        command.Parameters.AddWithValue("$name", name.Trim());
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result) != 0;
    }

    public async Task AddAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        if (profile.IsCatalogMaster)
        {
            throw new InvalidOperationException("Catalog Master is a built-in profile.");
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Profiles (Id, Name, IsCatalogMaster, PasswordHash, RecoveryEmail)
            VALUES ($id, $name, 0, $passwordHash, $recoveryEmail);
            """;
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$passwordHash", (object?)profile.PasswordHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$recoveryEmail", (object?)profile.RecoveryEmail ?? DBNull.Value);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new DuplicateProfileNameException(profile.Name);
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Profiles WHERE Id = $id AND IsCatalogMaster = 0;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static Profile ReadProfile(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetInt64(2) == 1,
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4));
}