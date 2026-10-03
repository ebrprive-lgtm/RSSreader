namespace RssReader.Domain;

public static class ProfileNameValidator
{
    public const string CatalogMasterName = "Catalog Master";

    public static ProfileNameValidationResult Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new(ProfileNameError.Required, string.Empty);
        }

        var normalizedName = name.Trim();
        if (normalizedName.Any(character => !char.IsLetterOrDigit(character) && character != ' '))
        {
            return new(ProfileNameError.InvalidCharacters, normalizedName);
        }

        if (string.Equals(normalizedName, CatalogMasterName, StringComparison.OrdinalIgnoreCase))
        {
            return new(ProfileNameError.Reserved, normalizedName);
        }

        return new(ProfileNameError.None, normalizedName);
    }
}