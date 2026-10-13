namespace RssReader.Application;

public sealed record LibraryRestoreResult(
    bool Applied,
    string? SafetyBackupPath,
    string? ErrorMessage);
