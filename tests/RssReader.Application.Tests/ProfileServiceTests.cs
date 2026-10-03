using RssReader.Application;
using RssReader.Domain;

namespace RssReader.Application.Tests;

[TestClass]
public sealed class ProfileServiceTests
{
    [TestMethod]
    public async Task GetAvailableProfiles_HidesCatalogMasterUnlessRequested()
    {
        var service = CreateService();
        await service.InitializeAsync();

        var hidden = await service.GetAvailableProfilesAsync(revealCatalogMaster: false);
        var revealed = await service.GetAvailableProfilesAsync(revealCatalogMaster: true);

        Assert.AreEqual(0, hidden.Count);
        Assert.AreEqual(1, revealed.Count);
        Assert.IsTrue(revealed[0].IsCatalogMaster);
    }

    [TestMethod]
    public async Task CreateProfile_NormalizesNameAndStoresOnlyPasswordHash()
    {
        var service = CreateService();
        await service.InitializeAsync();

        var profile = await service.CreateProfileAsync("  Ada Lovelace  ", "secret", " ada@example.com ");

        Assert.AreEqual("Ada Lovelace", profile.Name);
        Assert.AreEqual("hash:secret", profile.PasswordHash);
        Assert.AreEqual("ada@example.com", profile.RecoveryEmail);
        Assert.IsFalse(profile.IsCatalogMaster);
    }

    [TestMethod]
    public async Task CreateProfile_RejectsDuplicateNameIgnoringCase()
    {
        var service = CreateService();
        await service.InitializeAsync();
        await service.CreateProfileAsync("Reader", null, null);

        await Assert.ThrowsExceptionAsync<DuplicateProfileNameException>(
            () => service.CreateProfileAsync("reader", null, null));
    }

    [TestMethod]
    public async Task OpenProfile_RequiresCorrectPasswordWhenHashExists()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var profile = await service.CreateProfileAsync("Reader", "secret", null);

        var wrongPassword = await service.OpenProfileAsync(profile.Id, "wrong");
        var correctPassword = await service.OpenProfileAsync(profile.Id, "secret");

        Assert.IsNull(wrongPassword);
        Assert.AreEqual(profile.Id, correctPassword?.Id);
    }

    [TestMethod]
    public async Task OpenProfile_OpensProfileWithoutPassword()
    {
        var service = CreateService();
        await service.InitializeAsync();
        var profile = await service.CreateProfileAsync("Reader", null, null);

        var opened = await service.OpenProfileAsync(profile.Id, null);

        Assert.AreEqual(profile.Id, opened?.Id);
    }

    [TestMethod]
    public async Task DeleteProfileRemovesRegularProfileAndProtectsCatalogMaster()
    {
        var store = new FakeProfileStore();
        var service = new ProfileService(store, new FakePasswordHasher());
        await service.InitializeAsync();
        var profile = await service.CreateProfileAsync("Reader", null, null);
        var catalogMaster = (await service.GetAvailableProfilesAsync(revealCatalogMaster: true))
            .Single(candidate => candidate.IsCatalogMaster);

        await service.DeleteProfileAsync(profile.Id);

        Assert.IsNull(await store.GetByIdAsync(profile.Id));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.DeleteProfileAsync(catalogMaster.Id));
        Assert.IsNotNull(await store.GetByIdAsync(catalogMaster.Id));
    }

    private static ProfileService CreateService() => new(new FakeProfileStore(), new FakePasswordHasher());

    private sealed class FakeProfileStore : IProfileStore
    {
        private readonly List<Profile> _profiles = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_profiles.All(profile => !profile.IsCatalogMaster))
            {
                _profiles.Add(Profile.CreateCatalogMaster());
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Profile>>(_profiles.ToArray());

        public Task<Profile?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.SingleOrDefault(profile => profile.Id == id));

        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default) =>
            Task.FromResult(_profiles.Any(profile => string.Equals(
                profile.Name,
                name,
                StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(Profile profile, CancellationToken cancellationToken = default)
        {
            _profiles.Add(profile);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            _profiles.RemoveAll(profile => profile.Id == id && !profile.IsCatalogMaster);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hash:{password}";

        public bool Verify(string encodedHash, string password) => encodedHash == Hash(password);
    }
}