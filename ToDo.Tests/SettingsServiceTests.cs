using System;
using System.IO;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises the settings persistence and db-path migration logic (the user-data-critical
/// parts) in an isolated temp directory via SettingsService.UseDirectory.
/// </summary>
[Collection("settings-shared")]   // serialized with SyncServiceTests — SettingsService is a shared static
public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsServiceTests() => SettingsService.UseDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void FirstLoad_CreatesDir_PrefillsSources_AndWritesFile()
    {
        SettingsService.Load();

        Assert.True(Directory.Exists(_dir));
        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
        Assert.Equal(SettingsService.DefaultUpdateSources.Count, SettingsService.Current.UpdateSources.Count);
        Assert.Equal(SettingsService.DefaultUpdateSources[0].Url, SettingsService.Current.UpdateSources[0].Url);
        Assert.Equal(Path.Combine(_dir, "todo.db"), SettingsService.DefaultDbPath);
        Assert.Equal(SettingsService.DefaultDbPath, SettingsService.Current.DbPath);
    }

    [Fact]
    public void SaveThenReload_PreservesValues()
    {
        SettingsService.Load();
        SettingsService.Current.Language = "English";
        SettingsService.Current.Theme = "Dark";
        SettingsService.Current.SidebarWidth = 360;
        SettingsService.Save();

        SettingsService.UseDirectory(_dir); // reset cached state so Load re-reads from disk
        SettingsService.Load();

        Assert.Equal("English", SettingsService.Current.Language);
        Assert.Equal("Dark", SettingsService.Current.Theme);
        Assert.Equal(360, SettingsService.Current.SidebarWidth);
    }

    [Fact]
    public void SetDbPath_MigratesExistingDb_WhenTargetMissing()
    {
        SettingsService.Load();
        var oldPath = SettingsService.DefaultDbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(oldPath)!);
        File.WriteAllText(oldPath, "db-content");

        var newPath = Path.Combine(_dir, "nested", "data.db");
        SettingsService.SetDbPath(newPath);

        Assert.Equal(newPath, SettingsService.Current.DbPath);
        Assert.True(File.Exists(newPath));
        Assert.Equal("db-content", File.ReadAllText(newPath));
        Assert.True(File.Exists(oldPath)); // File.Copy keeps the source (backup), does not move
    }

    [Fact]
    public void SetDbPath_WithNoSourceFile_JustSwitchesPath()
    {
        SettingsService.Load();
        var newPath = Path.Combine(_dir, "new.db");
        SettingsService.SetDbPath(newPath);

        Assert.Equal(newPath, SettingsService.Current.DbPath);
        Assert.False(File.Exists(newPath)); // nothing to migrate
    }

    [Fact]
    public void SetDbPath_SamePath_DoesNotOverwrite()
    {
        SettingsService.Load();
        var path = SettingsService.DefaultDbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "original");

        SettingsService.SetDbPath(path); // no-op

        Assert.Equal("original", File.ReadAllText(path));
    }

    [Fact]
    public void Load_FromLegacyFile_StampsSchemaAndDefaultsDbPath()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            "{\"Theme\":\"Dark\",\"Language\":\"English\"}");

        SettingsService.Load();

        Assert.Equal(4, SettingsService.Current.SchemaVersion);
        Assert.Equal("Dark", SettingsService.Current.Theme);
        Assert.Equal("English", SettingsService.Current.Language);
        Assert.Equal(SettingsService.DefaultDbPath, SettingsService.Current.DbPath);
        // New Behavior fields fall back to their property defaults on a legacy file
        Assert.True(SettingsService.Current.MinimizeToTrayOnClose);
        Assert.True(SettingsService.Current.StickyShowTags);
    }

    [Fact]
    public void Load_FromCorruptFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{{{ not json");

        SettingsService.Load();

        Assert.NotNull(SettingsService.Current);
        Assert.Equal(SettingsService.DefaultDbPath, SettingsService.Current.DbPath);
    }
}
