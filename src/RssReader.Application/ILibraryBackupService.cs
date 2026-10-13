namespace RssReader.Application;

public interface ILibraryBackupService
{
    long GetLocalStorageBytes();

    Task CreateBackupAsync(string backupPath, CancellationToken cancellationToken = default);

    Task StageRestoreAsync(string backupPath, CancellationToken cancellationToken = default);

    Task<LibraryRestoreResult?> ApplyPendingRestoreAsync(CancellationToken cancellationToken = default);
}
