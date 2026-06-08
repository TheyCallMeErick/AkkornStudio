using System.Text;
using System.Threading;
using System.Reflection;
using AkkornStudio.UI.Serialization;
using Xunit;

namespace AkkornStudio.Tests.Unit.Serialization;

[Collection("StoreSerialization")]
public class StoreCorruptionFallbackTests
{
    private static void WriteAllTextWithRetry(string path, string content, int maxAttempts = 5, int delayMs = 25)
    {
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.WriteAllText(path, content, Encoding.UTF8);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(delayMs);
            }
        }

        File.WriteAllText(path, content, Encoding.UTF8);
    }

    [Fact]
    public void SnippetStore_Load_WithCorruptJson_ReturnsEmptyAndRaisesWarning()
    {
        string path = (string)typeof(SnippetStore)
            .GetMethod("StoreFilePath", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
        string dir = Path.GetDirectoryName(path)!;
        string backup = path + ".bak-test-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(dir);
        try
        {
            if (File.Exists(path))
                File.Copy(path, backup, overwrite: true);

            WriteAllTextWithRetry(path, "{ not-valid-json");

            string? warning = null;
            void Handler(string msg) => warning = msg;
            SnippetStore.WarningRaised += Handler;
            try
            {
                var loaded = SnippetStore.Load();
                Assert.Empty(loaded);
                Assert.False(string.IsNullOrWhiteSpace(warning));
            }
            finally
            {
                SnippetStore.WarningRaised -= Handler;
            }
        }
        finally
        {
            if (File.Exists(backup))
            {
                File.Copy(backup, path, overwrite: true);
                File.Delete(backup);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void FlowVersionStore_Load_WithCorruptJson_ReturnsEmptyAndRaisesWarning()
    {
        string path = (string)typeof(FlowVersionStore)
            .GetMethod("StorePath", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
        string dir = Path.GetDirectoryName(path)!;
        string backup = path + ".bak-test-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(dir);
        try
        {
            if (File.Exists(path))
                File.Copy(path, backup, overwrite: true);

            WriteAllTextWithRetry(path, "{ not-valid-json");

            string? warning = null;
            void Handler(string msg) => warning = msg;
            FlowVersionStore.WarningRaised += Handler;
            try
            {
                var loaded = FlowVersionStore.Load();
                Assert.Empty(loaded);
                Assert.False(string.IsNullOrWhiteSpace(warning));
            }
            finally
            {
                FlowVersionStore.WarningRaised -= Handler;
            }
        }
        finally
        {
            if (File.Exists(backup))
            {
                File.Copy(backup, path, overwrite: true);
                File.Delete(backup);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
