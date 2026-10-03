namespace RssReader.Domain;

public sealed record Profile(
    string Id,
    string Name,
    bool IsCatalogMaster,
    string? PasswordHash = null,
    string? RecoveryEmail = null)
{
    public static Profile CreateCatalogMaster() => new(
        "catalog-master",
        ProfileNameValidator.CatalogMasterName,
        true);

    public static Profile CreateRegular(
        string name,
        string? passwordHash = null,
        string? recoveryEmail = null)
    {
        var validation = ProfileNameValidator.Validate(name);
        if (!validation.IsValid)
        {
            throw new ArgumentException($"Invalid profile name: {validation.Error}.", nameof(name));
        }

        return new(
            Guid.NewGuid().ToString("N"),
            validation.NormalizedName,
            false,
            passwordHash,
            string.IsNullOrWhiteSpace(recoveryEmail) ? null : recoveryEmail.Trim());
    }
}