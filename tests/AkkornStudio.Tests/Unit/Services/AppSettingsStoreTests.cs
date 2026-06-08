using AkkornStudio.UI.Services.Settings;
using AkkornStudio.UI.Services.Input.ShortcutRegistry;
using AkkornStudio.UI.ViewModels;

namespace AkkornStudio.Tests.Unit.Services;

public class AppSettingsStoreTests
{
    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            AppSettings settings = AppSettingsStore.Load();
            Assert.Equal("Dark", settings.ThemeVariant);
            Assert.True(settings.SqlEditorTop1000WithoutWhereEnabled);
            Assert.True(settings.SqlEditorProtectMutationWithoutWhereEnabled);
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsThemeVariant()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            AppSettingsStore.Save(new AppSettings { ThemeVariant = "Light" });
            AppSettings loaded = AppSettingsStore.Load();

            Assert.Equal("Light", loaded.ThemeVariant);
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveThemeVariant_Blank_FallsBackToDark()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            AppSettingsStore.SaveThemeVariant("   ");
            AppSettings loaded = AppSettingsStore.Load();

            Assert.Equal("Dark", loaded.ThemeVariant);
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_WithoutShortcutSection_UsesShortcutDefaults()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(file, """
{
  "themeVariant": "Light"
}
""");

            AppSettings settings = AppSettingsStore.Load();

            Assert.NotNull(settings.Shortcuts);
            Assert.Equal(1, settings.Shortcuts.Version);
            Assert.Empty(settings.Shortcuts.Overrides);
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Save_ThenLoad_RoundTripsShortcutOverrides()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            AppSettings settings = new()
            {
                ThemeVariant = "Dark",
                Shortcuts = new ShortcutSettingsSection
                {
                    Version = 1,
                    Overrides =
                    [
                        new("shell.commandPalette.open", "Ctrl+Shift+K"),
                        new("canvas.zoom.reset", "Ctrl+9"),
                    ],
                },
            };

            AppSettingsStore.Save(settings);
            AppSettings loaded = AppSettingsStore.Load();

            Assert.Equal(2, loaded.Shortcuts.Overrides.Count);
            Assert.Contains(loaded.Shortcuts.Overrides, item =>
                item.ActionId == "shell.commandPalette.open" && item.Gesture == "Ctrl+Shift+K");
            Assert.Contains(loaded.Shortcuts.Overrides, item =>
                item.ActionId == "canvas.zoom.reset" && item.Gesture == "Ctrl+9");
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoadSqlEditorExecutionHistory_PerProfile_RoundTripsAndCapsToFiveHundred()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            List<SqlEditorHistoryEntry> entries = Enumerable.Range(1, 650)
                .Select(i => new SqlEditorHistoryEntry(
                    Sql: $"SELECT {i}",
                    Success: true,
                    RowsAffected: 1,
                    ExecutionTime: TimeSpan.FromMilliseconds(i),
                    ExecutedAt: DateTimeOffset.UtcNow.AddMinutes(-i)))
                .ToList();

            AppSettingsStore.SaveSqlEditorExecutionHistory("profile-a", entries);
            IReadOnlyList<SqlEditorHistoryEntry> loaded = AppSettingsStore.LoadSqlEditorExecutionHistory("profile-a");

            Assert.Equal(500, loaded.Count);
            Assert.True(loaded[0].ExecutedAt >= loaded[^1].ExecutedAt);
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ClearSqlEditorExecutionHistory_RemovesOnlyTargetProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            SqlEditorHistoryEntry entry = new("SELECT 1", true, 1, TimeSpan.FromMilliseconds(1), DateTimeOffset.UtcNow);
            AppSettingsStore.SaveSqlEditorExecutionHistory("profile-a", [entry]);
            AppSettingsStore.SaveSqlEditorExecutionHistory("profile-b", [entry with { Sql = "SELECT 2" }]);

            AppSettingsStore.ClearSqlEditorExecutionHistory("profile-a");

            Assert.Empty(AppSettingsStore.LoadSqlEditorExecutionHistory("profile-a"));
            Assert.Single(AppSettingsStore.LoadSqlEditorExecutionHistory("profile-b"));
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoadSqlEditorSafetySettings_RoundTripsFlags()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            AppSettingsStore.SaveSqlEditorSafetySettings(top1000WithoutWhereEnabled: false, protectMutationWithoutWhereEnabled: false);
            (bool top1000Enabled, bool guardEnabled) = AppSettingsStore.LoadSqlEditorSafetySettings();

            Assert.False(top1000Enabled);
            Assert.False(guardEnabled);
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveAndLoadSqlEditorResultDateTimeDisplaySettings_RoundTripsAndNormalizes()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        try
        {
            AppSettingsStore.SaveSqlEditorResultDateTimeDisplaySettings(new SqlEditorResultDateTimeDisplaySettings
            {
                DateOrder = "dmy",
                DateSeparator = ".",
                PreferRawValues = true,
            });

            SqlEditorResultDateTimeDisplaySettings loaded = AppSettingsStore.LoadSqlEditorResultDateTimeDisplaySettings();
            Assert.Equal("DMY", loaded.DateOrder);
            Assert.Equal("-", loaded.DateSeparator);
            Assert.True(loaded.PreferRawValues);
        }
        finally
        {
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SaveSqlEditorResultDateTimeDisplaySettings_RaisesChangedEvent_OnlyWhenEffectiveValueChanges()
    {
        string root = Path.Combine(Path.GetTempPath(), "vsa-settings-tests", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "app.settings.json");
        AppSettingsStore.SettingsPathOverride = file;

        int raisedCount = 0;
        SqlEditorResultDateTimeDisplaySettings? raisedSettings = null;
        EventHandler<SqlEditorResultDateTimeDisplaySettings> handler = (_, settings) =>
        {
            raisedCount++;
            raisedSettings = settings;
        };

        AppSettingsStore.SqlEditorResultDateTimeDisplaySettingsChanged += handler;
        try
        {
            AppSettingsStore.SaveSqlEditorResultDateTimeDisplaySettings(new SqlEditorResultDateTimeDisplaySettings
            {
                DateOrder = "DMY",
                DateSeparator = "/",
                PreferRawValues = false,
            });
            AppSettingsStore.SaveSqlEditorResultDateTimeDisplaySettings(new SqlEditorResultDateTimeDisplaySettings
            {
                DateOrder = "dmy",
                DateSeparator = "/",
                PreferRawValues = false,
            });
            AppSettingsStore.SaveSqlEditorResultDateTimeDisplaySettings(new SqlEditorResultDateTimeDisplaySettings
            {
                DateOrder = "MDY",
                DateSeparator = "/",
                PreferRawValues = true,
            });

            Assert.Equal(2, raisedCount);
            Assert.NotNull(raisedSettings);
            Assert.Equal("MDY", raisedSettings!.DateOrder);
            Assert.Equal("/", raisedSettings.DateSeparator);
            Assert.True(raisedSettings.PreferRawValues);
        }
        finally
        {
            AppSettingsStore.SqlEditorResultDateTimeDisplaySettingsChanged -= handler;
            AppSettingsStore.SettingsPathOverride = null;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
