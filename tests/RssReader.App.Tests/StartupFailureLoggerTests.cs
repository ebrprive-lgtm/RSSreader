using System.IO;
using RssReader.App;

namespace RssReader.App.Tests;

[TestClass]
public sealed class StartupFailureLoggerTests
{
    [TestMethod]
    public async Task WriteAsync_PersistsStartupFailureDetailsLocally()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"rss-reader-logs-{Guid.NewGuid():N}");
        try
        {
            var exception = new InvalidOperationException("Database initialization failed.");

            var logPath = await StartupFailureLogger.WriteAsync(directory, exception);

            Assert.IsNotNull(logPath);
            Assert.IsTrue(File.Exists(logPath));
            var logContent = await File.ReadAllTextAsync(logPath);
            StringAssert.Contains(logContent, nameof(InvalidOperationException));
            StringAssert.Contains(logContent, exception.Message);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
