namespace RssReader.Application;

public sealed class DuplicateProfileNameException(string name)
    : Exception($"A profile named '{name}' already exists.");