using RssReader.Domain;

namespace RssReader.Application;

public sealed class ProfileService(IProfileStore store, IPasswordHasher passwordHasher)
{
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        store.InitializeAsync(cancellationToken);

    public Task<ProfilePreferences> GetPreferencesAsync(
        string profileId,
        CancellationToken cancellationToken = default) =>
        store.GetPreferencesAsync(profileId, cancellationToken);

    public Task SavePreferencesAsync(
        string profileId,
        ProfilePreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!Enum.IsDefined(preferences.StartPage) ||
            !Enum.IsDefined(preferences.Presentation) ||
            !Enum.IsDefined(preferences.Sort))
        {
            throw new ArgumentOutOfRangeException(nameof(preferences), "A profile preference has an unsupported value.");
        }

        if (preferences.FolderArticleLimitPerFeed is < ProfilePreferences.MinimumFolderArticleLimitPerFeed or
            > ProfilePreferences.MaximumFolderArticleLimitPerFeed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferences),
                $"The folder article limit must be between {ProfilePreferences.MinimumFolderArticleLimitPerFeed} and {ProfilePreferences.MaximumFolderArticleLimitPerFeed}.");
        }

            if (preferences.AutoRefreshIntervalMinutes is not (0 or 15 or 30 or 60 or 240))
            {
                throw new ArgumentOutOfRangeException(nameof(preferences), "The auto-refresh interval is not supported.");
            }

        return store.SavePreferencesAsync(profileId, preferences, cancellationToken);
    }

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

    public async Task DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        var profile = await store.GetByIdAsync(id, cancellationToken);
        if (profile is null)
        {
            return;
        }

        if (profile.IsCatalogMaster)
        {
            throw new InvalidOperationException("Catalog Master cannot be deleted.");
        }

        await store.DeleteAsync(id, cancellationToken);
    }
}