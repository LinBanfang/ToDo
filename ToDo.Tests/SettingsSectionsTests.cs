using System;
using System.IO;
using System.Linq;
using ToDo.Services;
using ToDo.ViewModels;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises the settings-page section view-models: their setters persist to
/// SettingsService (and the file), and their derived display properties behave
/// (sound label, restart-to-apply hint, masked DB path). UI-bound commands that
/// open dialogs / play sound / hit the network are out of scope here.
/// </summary>
[Collection("settings-shared")]   // serialized with the other SettingsService users
public sealed class SettingsSectionsTests : IDisposable
{
    private readonly string _dir;

    public SettingsSectionsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "todo-settings-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        SettingsService.UseDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ─── BehaviorSection: each toggle persists + round-trips ───

    [Theory]
    [InlineData(nameof(BehaviorSection.MinimizeToTrayOnClose))]
    [InlineData(nameof(BehaviorSection.StickyShowTags))]
    [InlineData(nameof(BehaviorSection.ShowTaskTags))]
    [InlineData(nameof(BehaviorSection.ShowTaskSteps))]
    [InlineData(nameof(BehaviorSection.ShowTaskDue))]
    [InlineData(nameof(BehaviorSection.ShowTaskReminder))]
    [InlineData(nameof(BehaviorSection.ShowTaskNote))]
    [InlineData(nameof(BehaviorSection.ShowTaskAttachments))]
    public void BehaviorSection_Toggle_PersistsToSettings_AndRaises(string propName)
    {
        var section = new BehaviorSection();
        var prop = typeof(BehaviorSection).GetProperty(propName)!;
        var original = (bool)prop.GetValue(section)!;
        var flipped = !original;

        var raised = new List<string?>();
        section.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        prop.SetValue(section, flipped);

        Assert.Equal(flipped, (bool)prop.GetValue(section)!);
        Assert.Equal(flipped, (bool)typeof(AppSettings).GetProperty(propName)!.GetValue(SettingsService.Current)!);
        Assert.Contains(propName, raised);

        // Round-trip: reload from the file that Save() wrote.
        SettingsService.UseDirectory(_dir);
        Assert.Equal(flipped, (bool)typeof(AppSettings).GetProperty(propName)!.GetValue(SettingsService.Current)!);
    }

    // ─── ReminderSection ───────────────────────────────────

    [Fact]
    public void ReminderSection_Toggles_Persist()
    {
        var section = new ReminderSection();
        section.ReminderNotifications = false;
        section.ReminderSound = false;

        Assert.False(SettingsService.Current.ReminderNotifications);
        Assert.False(SettingsService.Current.ReminderSound);

        SettingsService.UseDirectory(_dir);   // re-read from the file Save() wrote
        Assert.False(SettingsService.Current.ReminderNotifications);
        Assert.False(SettingsService.Current.ReminderSound);
    }

    [Fact]
    public void ReminderSection_ToastSeconds_Persists()
    {
        var section = new ReminderSection();
        Assert.Equal(5, section.ToastSeconds);                       // default
        Assert.Equal(5, SettingsService.Current.ReminderToastSeconds);
        Assert.Equal(4, section.ToastOptions.Count);                 // 5s / 10s / 30s / never

        section.ToastSeconds = 0;                                    // 不自动关闭
        Assert.Equal(0, SettingsService.Current.ReminderToastSeconds);

        SettingsService.UseDirectory(_dir);                          // round-trip from the saved file
        Assert.Equal(0, SettingsService.Current.ReminderToastSeconds);
    }

    [Fact]
    public void ReminderSection_SoundLabel_NoPath_ShowsDefaultRingtone()
    {
        SettingsService.Current.ReminderSoundPath = "";
        var section = new ReminderSection();

        Assert.Equal(Loc.DefaultRingtone, section.SoundLabel);
        Assert.False(section.HasCustomSound);
    }

    [Fact]
    public void ReminderSection_SoundLabel_ExistingFile_ShowsFileName()
    {
        var wav = Path.Combine(_dir, "ding.wav");
        File.WriteAllText(wav, "fake-wav");
        SettingsService.Current.ReminderSoundPath = wav;
        var section = new ReminderSection();

        Assert.Equal("ding.wav", section.SoundLabel);
        Assert.True(section.HasCustomSound);
    }

    [Fact]
    public void ReminderSection_SoundLabel_MissingFile_ShowsMissingSuffix()
    {
        SettingsService.Current.ReminderSoundPath = Path.Combine(_dir, "gone.wav");   // never created
        var section = new ReminderSection();

        Assert.Equal($"gone.wav ({Loc.SoundMissing})", section.SoundLabel);
        Assert.True(section.HasCustomSound);
    }

    [Fact]
    public void ReminderSection_ResetSound_ClearsPath_AndRaises()
    {
        SettingsService.Current.ReminderSoundPath = Path.Combine(_dir, "custom.wav");
        var section = new ReminderSection();
        var raised = new List<string?>();
        section.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        section.ResetSoundCommand.Execute(null);

        Assert.Equal("", SettingsService.Current.ReminderSoundPath);
        Assert.False(section.HasCustomSound);
        Assert.Contains(nameof(ReminderSection.SoundLabel), raised);
        Assert.Contains(nameof(ReminderSection.HasCustomSound), raised);
    }

    // ─── SyncSection ───────────────────────────────────────

    [Fact]
    public void SyncSection_PersistsSettings_AndFallsBackWhenNoAppSync()
    {
        var section = new SyncSection();

        Assert.Equal(Loc.SyncStatusDisabled, section.StatusText);   // App.Sync is null in tests
        Assert.Equal("", section.LastSyncText);

        section.SyncEnabled = true;
        section.SyncServerUrl = "https://example.com/sync";
        section.SyncKey = "s3cret";

        Assert.True(SettingsService.Current.SyncEnabled);
        Assert.Equal("https://example.com/sync", SettingsService.Current.SyncServerUrl);
        Assert.Equal("s3cret", SettingsService.Current.SyncKey);

        SettingsService.UseDirectory(_dir);   // round-trip from file
        Assert.True(SettingsService.Current.SyncEnabled);
        Assert.Equal("s3cret", SettingsService.Current.SyncKey);
    }

    [Fact]
    public void SyncSection_SyncNow_DoesNotThrow()
    {
        var section = new SyncSection();
        section.SyncNowCommand.Execute(null);   // App.Sync null → no-op round-trip
        Assert.True(true);
    }

    // ─── GeneralSection ────────────────────────────────────

    [Fact]
    public void GeneralSection_Language_Persists_AndHintTracksLiveLanguage()
    {
        // Loc.Language is cached at startup; the hint must reflect that it differs
        // from the picked language (restart-to-apply), regardless of which is live.
        var liveName = Loc.Language == AppLanguage.Chinese ? "Chinese" : "English";
        SettingsService.Current.Language = liveName;
        var section = new GeneralSection();

        Assert.Equal(liveName, section.Language);
        Assert.Equal("", section.LanguageHint);   // matches the live language → no hint

        var other = liveName == "Chinese" ? "English" : "Chinese";
        section.Language = other;
        Assert.Equal(other, SettingsService.Current.Language);
        Assert.Equal(Loc.RestartToApply, section.LanguageHint);   // differs → restart hint
    }

    // ─── AppearanceSection ─────────────────────────────────

    [Fact]
    public void AppearanceSection_Ctor_ReadsCurrentTheme()
    {
        SettingsService.Current.Theme = "Dark";
        var section = new AppearanceSection();

        Assert.Equal("Dark", section.Theme);
    }

    // ─── DataSection: DB path masking ──────────────────────

    [Fact]
    public void DataSection_DbPath_MasksLocalAppDataPrefix()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsService.Current.DbPath = Path.Combine(local, "ToDo", "todo.db");

        Assert.Equal("%LOCALAPPDATA%\\ToDo\\todo.db", new DataSection().DbPath);
    }

    [Fact]
    public void DataSection_DbPath_MasksUserProfilePrefix()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        SettingsService.Current.DbPath = Path.Combine(profile, "ToDo", "todo.db");

        Assert.Equal("%USERPROFILE%\\ToDo\\todo.db", new DataSection().DbPath);
    }

    [Fact]
    public void DataSection_DbPath_Unmasked_WhenNotUnderKnownPrefix()
    {
        SettingsService.Current.DbPath = @"C:\Data\todo.db";

        Assert.Equal(@"C:\Data\todo.db", new DataSection().DbPath);
    }

    // ─── UpdateSection ─────────────────────────────────────

    [Fact]
    public void UpdateSection_CheckForUpdatesOnStartup_Persists()
    {
        var section = new UpdateSection();
        var original = section.CheckForUpdatesOnStartup;

        section.CheckForUpdatesOnStartup = !original;

        Assert.Equal(!original, SettingsService.Current.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void UpdateSection_AddAndRemoveSource_PersistsList()
    {
        var section = new UpdateSection();
        var count = section.UpdateSources.Count;

        section.AddSourceCommand.Execute(null);
        Assert.Equal(count + 1, section.UpdateSources.Count);
        var newRow = section.UpdateSources[^1];
        Assert.Equal("github", newRow.Type);

        newRow.Url = "https://example.com/feed.xml";   // any edit persists the list
        Assert.Contains(SettingsService.Current.UpdateSources, s => s.Url == "https://example.com/feed.xml");

        section.RemoveSourceCommand.Execute(newRow);
        Assert.Equal(count, section.UpdateSources.Count);
        Assert.DoesNotContain(SettingsService.Current.UpdateSources, s => s.Url == "https://example.com/feed.xml");
    }
}
