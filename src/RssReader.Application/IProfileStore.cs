using RssReader.Domain;

namespace RssReader.Application;

public interface IProfileStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<ProfilePreferences> GetPreferencesAsync(string profileId, CancellationToken cancellationToken = default);

    Task SavePreferencesAsync(string profileId, ProfilePreferences preferences, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Profile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Profile?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);

    Task AddAsync(Profile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}