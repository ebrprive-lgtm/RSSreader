using RssReader.Domain;

namespace RssReader.Application;

public sealed class ProfileService(IProfileStore store, IPasswordHasher passwordHasher)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        store.InitializeAsync(cancellationToken);

    public async Task<IReadOnlyList<Profile>> GetAvailableProfilesAsync(
        bool revealCatalogMaster,
        CancellationToken cancellationToken = default)
    {
        var profiles = await store.GetAllAsync(cancellationToken);
        return profiles
            .Where(profile => revealCatalogMaster || !profile.IsCatalogMaster)
            .OrderBy(profile => profile.IsCatalogMaster)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<Profile> CreateProfileAsync(
        string name,
        string? password,
        string? recoveryEmail,
        CancellationToken cancellationToken = default)
    {
        var validation = ProfileNameValidator.Validate(name);
        if (!validation.IsValid)
        {
            throw new ProfileNameValidationException(validation.Error);
        }

        if (await store.NameExistsAsync(validation.NormalizedName, cancellationToken))
        {
            throw new DuplicateProfileNameException(validation.NormalizedName);
        }

        var passwordHash = string.IsNullOrEmpty(password) ? null : passwordHasher.Hash(password);
        var profile = Profile.CreateRegular(validation.NormalizedName, passwordHash, recoveryEmail);
        await store.AddAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<Profile?> OpenProfileAsync(
        string id,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var profile = await store.GetByIdAsync(id, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        if (profile.PasswordHash is null)
        {
            return profile;
        }

        return !string.IsNullOrEmpty(password) && passwordHasher.Verify(profile.PasswordHash, password)
            ? profile
            : null;
    }
}