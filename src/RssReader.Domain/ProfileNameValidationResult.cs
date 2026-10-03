namespace RssReader.Domain;

public readonly record struct ProfileNameValidationResult(
    ProfileNameError Error,
    string NormalizedName)
{
    public bool IsValid => Error == ProfileNameError.None;
}