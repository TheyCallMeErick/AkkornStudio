using System.Reflection;
using AkkornStudio.UI.Services;
using Xunit;

namespace AkkornStudio.Tests.Unit.Services;

[Collection("StoreSerialization")]
public sealed class RecentFilesStoreTests
{
    [Fact]
    public void GetRecent_WhenJsonIsCorrupted_RaisesWarningAndReturnsEmpty()
    {
        string path = ResolveRecentFilePath();
        bool fileExisted = File.Exists(path);
        bool dirExistedAtPath = Directory.Exists(path);
        string? backup = fileExisted ? File.ReadAllText(path) : null;
        string? warning = null;

        try
        {
            string? parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
                Directory.CreateDirectory(parent);

            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);

            File.WriteAllText(path, "{ invalid-json");

            void Handler(string message) => warning = message;
            RecentFilesStore.WarningRaised += Handler;
            try
            {
                IReadOnlyList<RecentFileEntry> recent = RecentFilesStore.GetRecent();
                Assert.Empty(recent);
                Assert.False(string.IsNullOrWhiteSpace(warning));
                Assert.Contains("recent files store", warning!, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                RecentFilesStore.WarningRaised -= Handler;
            }
        }
        finally
        {
            RestoreRecentFilePath(path, fileExisted, dirExistedAtPath, backup);
        }
    }

    [Fact]
    public void Touch_WhenPersistFails_RaisesWarning()
    {
        string path = ResolveRecentFilePath();
        bool fileExisted = File.Exists(path);
        bool dirExistedAtPath = Directory.Exists(path);
        string? backup = fileExisted ? File.ReadAllText(path) : null;
        string? warning = null;

        try
        {
            if (File.Exists(path))
                File.Delete(path);

            Directory.CreateDirectory(path);

            void Handler(string message) => warning = message;
            RecentFilesStore.WarningRaised += Handler;
            try
            {
                RecentFilesStore.Touch(Path.Combine(Path.GetTempPath(), "recent-test.sql"));
                Assert.False(string.IsNullOrWhiteSpace(warning));
                Assert.Contains("recent files store", warning!, StringComparison.OrdinalIgnoreCase);
                Assert.Empty(ListRecentTempFiles(path));
            }
            finally
            {
                RecentFilesStore.WarningRaised -= Handler;
            }
        }
        finally
        {
            RestoreRecentFilePath(path, fileExisted, dirExistedAtPath, backup);
        }
    }

    private static string ResolveRecentFilePath()
    {
        PropertyInfo property = typeof(RecentFilesStore).GetProperty(
            "RecentFilePath",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        return (string)property.GetValue(null)!;
    }

    private static void RestoreRecentFilePath(
        string path,
        bool fileExisted,
        bool dirExistedAtPath,
        string? backup)
    {
        if (Directory.Exists(path) && !dirExistedAtPath)
            Directory.Delete(path, recursive: true);

        if (fileExisted)
            File.WriteAllText(path, backup ?? string.Empty);
        else if (File.Exists(path))
            File.Delete(path);

        foreach (string temp in ListRecentTempFiles(path))
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static IReadOnlyList<string> ListRecentTempFiles(string recentFilePath)
    {
        string? dir = Path.GetDirectoryName(recentFilePath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return [];

        string fileName = Path.GetFileName(recentFilePath);
        return Directory.GetFiles(dir, $"{fileName}.tmp-*", SearchOption.TopDirectoryOnly);
    }
}
