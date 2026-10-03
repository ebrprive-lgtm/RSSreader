using RssReader.Domain;

namespace RssReader.Application;

public sealed class ProfileNameValidationException(ProfileNameError error)
    : Exception($"Invalid profile name: {error}.")
{
    public ProfileNameError Error { get; } = error;
}