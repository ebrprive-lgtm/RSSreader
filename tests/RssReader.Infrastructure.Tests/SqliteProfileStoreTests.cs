using RssReader.Application;
using RssReader.Domain;
using RssReader.Infrastructure;
using Microsoft.Data.Sqlite;

namespace RssReader.Infrastructure.Tests;

[TestClass]
public sealed class SqliteProfileStoreTests
{
    [TestMethod]
    public async Task Initialize_IsIdempotentAndSeedsCatalogMaster()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteProfileStore(database.Path);

        await store.InitializeAsync();
        await store.InitializeAsync();
        var profiles = await store.GetAllAsync();

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual(ProfileNameValidator.CatalogMasterName, profiles[0].Name);
        Assert.IsTrue(profiles[0].IsCatalogMaster);
        Assert.IsNull(profiles[0].PasswordHash);
    }

    [TestMethod]
    public async Task AddProfile_PersistsAndFindsProfileWithoutCaseSensitiveNameMatching()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteProfileStore(database.Path);
        await store.InitializeAsync();
        var profile = Profile.CreateRegular("Reader", "hashed-value", "reader@example.com");

        await store.AddAsync(profile);

        Assert.IsTrue(await store.NameExistsAsync("reader"));
        var loaded = await store.GetByIdAsync(profile.Id);
        Assert.AreEqual(profile, loaded);
    }

    [TestMethod]
    public async Task AddProfile_RejectsDuplicateNameIgnoringCase()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteProfileStore(database.Path);
        await store.InitializeAsync();
        await store.AddAsync(Profile.CreateRegular("Reader"));

        await Assert.ThrowsExceptionAsync<DuplicateProfileNameException>(
            () => store.AddAsync(Profile.CreateRegular("reader")));
    }

    [TestMethod]
    public void Pbkdf2PasswordHasher_VerifiesPasswordAndRejectsInvalidHashes()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("secret");

        Assert.AreNotEqual("secret", hash);
        Assert.IsTrue(hasher.Verify(hash, "secret"));
        Assert.IsFalse(hasher.Verify(hash, "wrong"));
        Assert.IsFalse(hasher.Verify("not-a-hash", "secret"));
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"rss-reader-{Guid.NewGuid():N}.db");

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in new[] { Path, $"{Path}-shm", $"{Path}-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}