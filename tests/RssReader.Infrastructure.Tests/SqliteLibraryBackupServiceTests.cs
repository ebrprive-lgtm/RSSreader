using Microsoft.Data.Sqlite;
using RssReader.Application;
using RssReader.Domain;
using RssReader.Infrastructure;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class SqliteLibraryBackupServiceTests
{
    [TestMethod]
    public async Task CreateBackup_WritesAConsistentCopyOfTheWholeLibrary()
    {
        using var library = new TemporaryLibrary();
        var profileStore = await InitializeLibraryAsync(library.DatabasePath);
        var profile = Profile.CreateRegular("Backup profile");
        await profileStore.AddAsync(profile);
        await profileStore.SavePreferencesAsync(
            profile.Id,
            new ProfilePreferences(HideReadArticles: true));
        var backupService = new SqliteLibraryBackupService(library.DatabasePath);

        await backupService.CreateBackupAsync(library.BackupPath);

        Assert.IsTrue(File.Exists(library.BackupPath));
        Assert.IsTrue(new FileInfo(library.BackupPath).Length > 0);
        var reopenedStore = new SqliteProfileStore(library.BackupPath);
        Assert.IsTrue((await reopenedStore.GetAllAsync()).Any(item => item.Id == profile.Id));
        Assert.IsTrue((await reopenedStore.GetPreferencesAsync(profile.Id)).HideReadArticles);
        Assert.IsTrue(backupService.GetLocalStorageBytes() > 0);
    }

    [TestMethod]
    public async Task Restore_StagesValidatedBackupAndPreservesPreRestoreSafetyCopy()
    {
        using var source = new TemporaryLibrary();
        using var target = new TemporaryLibrary();
        var sourceStore = await InitializeLibraryAsync(source.DatabasePath);
        var importedProfile = Profile.CreateRegular("Imported profile");
        await sourceStore.AddAsync(importedProfile);
        await sourceStore.SavePreferencesAsync(
            importedProfile.Id,
            new ProfilePreferences(HideReadArticles: true));
        var targetStore = await InitializeLibraryAsync(target.DatabasePath);
        var existingProfile = Profile.CreateRegular("Existing profile");
        await targetStore.AddAsync(existingProfile);
        var sourceBackupService = new SqliteLibraryBackupService(source.DatabasePath);
        var targetBackupService = new SqliteLibraryBackupService(target.DatabasePath);
        await sourceBackupService.CreateBackupAsync(source.BackupPath);

        await targetBackupService.StageRestoreAsync(source.BackupPath);

        Assert.IsTrue((await targetStore.GetAllAsync()).Any(item => item.Id == existingProfile.Id));
        var restoreResult = await targetBackupService.ApplyPendingRestoreAsync();

        Assert.IsNotNull(restoreResult);
        Assert.IsTrue(restoreResult.Applied, restoreResult.ErrorMessage);
        Assert.IsNotNull(restoreResult.SafetyBackupPath);
        Assert.IsTrue(File.Exists(restoreResult.SafetyBackupPath));
        Assert.IsFalse(File.Exists($"{target.DatabasePath}.restore-pending"));
        var restoredStore = new SqliteProfileStore(target.DatabasePath);
        Assert.IsTrue((await restoredStore.GetAllAsync()).Any(item => item.Id == importedProfile.Id));
        Assert.IsTrue((await restoredStore.GetPreferencesAsync(importedProfile.Id)).HideReadArticles);
        Assert.IsFalse((await restoredStore.GetAllAsync()).Any(item => item.Id == existingProfile.Id));
        var safetyStore = new SqliteProfileStore(restoreResult.SafetyBackupPath);
        Assert.IsTrue((await safetyStore.GetAllAsync()).Any(item => item.Id == existingProfile.Id));
    }

    [TestMethod]
    public async Task ApplyPendingRestore_LeavesCurrentLibraryAvailableWhenStagedFileBecomesInvalid()
    {
        using var source = new TemporaryLibrary();
        using var target = new TemporaryLibrary();
        var sourceStore = await InitializeLibraryAsync(source.DatabasePath);
        await sourceStore.AddAsync(Profile.CreateRegular("Imported profile"));
        var targetStore = await InitializeLibraryAsync(target.DatabasePath);
        var existingProfile = Profile.CreateRegular("Existing profile");
        await targetStore.AddAsync(existingProfile);
        var sourceBackupService = new SqliteLibraryBackupService(source.DatabasePath);
        var targetBackupService = new SqliteLibraryBackupService(target.DatabasePath);
        await sourceBackupService.CreateBackupAsync(source.BackupPath);
        await targetBackupService.StageRestoreAsync(source.BackupPath);
        await File.WriteAllTextAsync($"{target.DatabasePath}.restore-pending", "invalid after staging");

        var restoreResult = await targetBackupService.ApplyPendingRestoreAsync();

        Assert.IsNotNull(restoreResult);
        Assert.IsFalse(restoreResult.Applied);
        Assert.IsNotNull(restoreResult.ErrorMessage);
        Assert.IsTrue((await targetStore.GetAllAsync()).Any(item => item.Id == existingProfile.Id));
        Assert.IsFalse(File.Exists($"{target.DatabasePath}.restore-pending"));
    }

    [TestMethod]
    public async Task StageRestore_RejectsInvalidBackupWithoutChangingCurrentLibrary()
    {
        using var library = new TemporaryLibrary();
        var profileStore = await InitializeLibraryAsync(library.DatabasePath);
        var existingProfile = Profile.CreateRegular("Existing profile");
        await profileStore.AddAsync(existingProfile);
        await File.WriteAllTextAsync(library.BackupPath, "not a database");
        var backupService = new SqliteLibraryBackupService(library.DatabasePath);

        await Assert.ThrowsExceptionAsync<InvalidDataException>(
            () => backupService.StageRestoreAsync(library.BackupPath));

        Assert.IsTrue((await profileStore.GetAllAsync()).Any(item => item.Id == existingProfile.Id));
        Assert.IsFalse(File.Exists($"{library.DatabasePath}.restore-pending"));
    }

    private static async Task<SqliteProfileStore> InitializeLibraryAsync(string databasePath)
    {
        var profileStore = new SqliteProfileStore(databasePath);
        await profileStore.InitializeAsync();
        await new SqliteCatalogStore(databasePath).InitializeAsync();
        await new SqliteReaderStore(databasePath).InitializeAsync();
        return profileStore;
    }

    private sealed class TemporaryLibrary : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"rss-reader-backup-{Guid.NewGuid():N}");

        public TemporaryLibrary()
        {
            Directory.CreateDirectory(_directory);
        }

        public string DatabasePath => Path.Combine(_directory, "rssreader.db");
        public string BackupPath => Path.Combine(_directory, "library.rssbackup");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
