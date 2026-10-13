using System.Diagnostics;
using System.IO;

namespace RssReader.App;

internal static class StartupFailureLogger
{
    internal static async Task<string?> WriteAsync(string localDataDirectory, Exception exception)
    {
        var logPath = Path.Combine(localDataDirectory, "RssReader", "Logs", "startup.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            await File.AppendAllTextAsync(
                logPath,
                $"{DateTimeOffset.UtcNow:O} Startup failure{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
            return logPath;
        }
        catch (IOException loggingException)
        {
            Trace.TraceError($"Startup failed: {exception}{Environment.NewLine}The diagnostic log could not be written: {loggingException}");
            return null;
        }
        catch (UnauthorizedAccessException loggingException)
        {
            Trace.TraceError($"Startup failed: {exception}{Environment.NewLine}The diagnostic log could not be written: {loggingException}");
            return null;
        }
    }
}
