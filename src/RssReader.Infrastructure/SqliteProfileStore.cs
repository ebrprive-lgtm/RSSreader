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
            CREATE TABLE IF NOT EXISTS ProfilePreferences (
                ProfileId TEXT NOT NULL PRIMARY KEY REFERENCES Profiles(Id) ON DELETE CASCADE,
                StartPage INTEGER NOT NULL CHECK (StartPage BETWEEN 0 AND 2),
                Presentation INTEGER NOT NULL CHECK (Presentation BETWEEN 0 AND 2),
                ArticleSort INTEGER NOT NULL CHECK (ArticleSort BETWEEN 0 AND 1),
                HideReadArticles INTEGER NOT NULL CHECK (HideReadArticles IN (0, 1)),
                FolderArticleLimitPerFeed INTEGER NOT NULL CHECK (FolderArticleLimitPerFeed BETWEEN 1 AND 100),
                RefreshFeedsWhenOpened INTEGER NOT NULL DEFAULT 1 CHECK (RefreshFeedsWhenOpened IN (0, 1)),
                AutoRefreshIntervalMinutes INTEGER NOT NULL DEFAULT 0 CHECK (AutoRefreshIntervalMinutes IN (0, 15, 30, 60, 240)),
                ShowRawFeedButton INTEGER NOT NULL DEFAULT 0 CHECK (ShowRawFeedButton IN (0, 1)),
                LimitArticleWidth INTEGER NOT NULL DEFAULT 1 CHECK (LimitArticleWidth IN (0, 1))
            );
            INSERT INTO Profiles (Id, Name, IsCatalogMaster, PasswordHash, RecoveryEmail)
            VALUES ($id, $name, 1, NULL, NULL)
            ON CONFLICT(Id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", Profile.CreateCatalogMaster().Id);
        command.Parameters.AddWithValue("$name", ProfileNameValidator.CatalogMasterName);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsurePreferenceColumnAsync(
            connection,
            "RefreshFeedsWhenOpened",
            "INTEGER NOT NULL DEFAULT 1 CHECK (RefreshFeedsWhenOpened IN (0, 1))",
            cancellationToken);
        await EnsurePreferenceColumnAsync(
            connection,
            "AutoRefreshIntervalMinutes",
            "INTEGER NOT NULL DEFAULT 0 CHECK (AutoRefreshIntervalMinutes IN (0, 15, 30, 60, 240))",
            cancellationToken);
        await EnsurePreferenceColumnAsync(
            connection,
            "ShowRawFeedButton",
            "INTEGER NOT NULL DEFAULT 0 CHECK (ShowRawFeedButton IN (0, 1))",
            cancellationToken);
        await EnsurePreferenceColumnAsync(
            connection,
            "LimitArticleWidth",
            "INTEGER NOT NULL DEFAULT 1 CHECK (LimitArticleWidth IN (0, 1))",
            cancellationToken);
    }

    public async Task<ProfilePreferences> GetPreferencesAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                 SELECT StartPage, Presentation, ArticleSort, HideReadArticles, FolderArticleLimitPerFeed,
                         RefreshFeedsWhenOpened, AutoRefreshIntervalMinutes, ShowRawFeedButton, LimitArticleWidth
            FROM ProfilePreferences
            WHERE ProfileId = $profileId;
            """;
        command.Parameters.AddWithValue("$profileId", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ProfilePreferences();
        }

        return new ProfilePreferences(
            (ProfileStartPage)reader.GetInt32(0),
            (ProfileArticlePresentation)reader.GetInt32(1),
            (ProfileArticleSort)reader.GetInt32(2),
            reader.GetInt64(3) == 1,
            reader.GetInt32(4),
            reader.GetInt64(5) == 1,
            reader.GetInt32(6),
            reader.GetInt64(7) == 1,
            reader.GetInt64(8) == 1);
    }

    public async Task SavePreferencesAsync(
        string profileId,
        ProfilePreferences preferences,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ProfilePreferences (
                ProfileId, StartPage, Presentation, ArticleSort, HideReadArticles, FolderArticleLimitPerFeed,
                RefreshFeedsWhenOpened, AutoRefreshIntervalMinutes, ShowRawFeedButton, LimitArticleWidth)
            VALUES ($profileId, $startPage, $presentation, $articleSort, $hideReadArticles, $folderArticleLimitPerFeed,
                    $refreshFeedsWhenOpened, $autoRefreshIntervalMinutes, $showRawFeedButton, $limitArticleWidth)
            ON CONFLICT(ProfileId) DO UPDATE SET
                StartPage = excluded.StartPage,
                Presentation = excluded.Presentation,
                ArticleSort = excluded.ArticleSort,
                HideReadArticles = excluded.HideReadArticles,
                FolderArticleLimitPerFeed = excluded.FolderArticleLimitPerFeed,
                RefreshFeedsWhenOpened = excluded.RefreshFeedsWhenOpened,
                AutoRefreshIntervalMinutes = excluded.AutoRefreshIntervalMinutes,
                ShowRawFeedButton = excluded.ShowRawFeedButton,
                LimitArticleWidth = excluded.LimitArticleWidth;
            """;
        command.Parameters.AddWithValue("$profileId", profileId);
        command.Parameters.AddWithValue("$startPage", (int)preferences.StartPage);
        command.Parameters.AddWithValue("$presentation", (int)preferences.Presentation);
        command.Parameters.AddWithValue("$articleSort", (int)preferences.Sort);
        command.Parameters.AddWithValue("$hideReadArticles", preferences.HideReadArticles ? 1 : 0);
        command.Parameters.AddWithValue("$folderArticleLimitPerFeed", preferences.FolderArticleLimitPerFeed);
        command.Parameters.AddWithValue("$refreshFeedsWhenOpened", preferences.RefreshFeedsWhenOpened ? 1 : 0);
        command.Parameters.AddWithValue("$autoRefreshIntervalMinutes", preferences.AutoRefreshIntervalMinutes);
        command.Parameters.AddWithValue("$showRawFeedButton", preferences.ShowRawFeedButton ? 1 : 0);
        command.Parameters.AddWithValue("$limitArticleWidth", preferences.LimitArticleWidth ? 1 : 0);
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

    private static async Task EnsurePreferenceColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using (var inspectCommand = connection.CreateCommand())
        {
            inspectCommand.CommandText = "PRAGMA table_info(ProfilePreferences);";
            await using var reader = await inspectCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE ProfilePreferences ADD COLUMN {columnName} {columnDefinition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Profile ReadProfile(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetInt64(2) == 1,
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4));
}