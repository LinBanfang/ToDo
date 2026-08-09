using System.IO;
using System.Text.Json;

namespace ToDo.Services;

public class AppSettings
{
    /// <summary>Bumped when the file gains fields; legacy files are stamped + saved on load.</summary>
    public int SchemaVersion { get; set; }
    public string DbPath { get; set; } = "";
    public string Theme { get; set; } = "Light"; // Light, Dark
    public double SidebarWidth { get; set; } = 280;
    public string Language { get; set; } = "Chinese"; // Chinese, English
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public bool ReminderNotifications { get; set; } = true;
    public bool ReminderSound { get; set; } = true;
    /// <summary>Optional WAV played for reminders; empty = system Exclamation sound.</summary>
    public string ReminderSoundPath { get; set; } = "";

    // ─── Behavior (tray / sticky note) ───────────────────
    /// <summary>Main window X hides to the tray instead of exiting the app.</summary>
    public bool MinimizeToTrayOnClose { get; set; } = true;
    /// <summary>Show colored tag pills on sticky-note task rows (default on).</summary>
    public bool StickyShowTags { get; set; } = true;

    // ─── Main-list task row meta toggles (default all on) ───
    /// <summary>Show tags on main-list task rows.</summary>
    public bool ShowTaskTags { get; set; } = true;
    /// <summary>Show the step progress (e.g. 1/3) on main-list task rows.</summary>
    public bool ShowTaskSteps { get; set; } = true;
    /// <summary>Show the due date on main-list task rows.</summary>
    public bool ShowTaskDue { get; set; } = true;
    /// <summary>Show the reminder on main-list task rows.</summary>
    public bool ShowTaskReminder { get; set; } = true;
    /// <summary>Show the note icon on main-list task rows.</summary>
    public bool ShowTaskNote { get; set; } = true;
    /// <summary>Show the attachment (paperclip) icon on main-list task rows.</summary>
    public bool ShowTaskAttachments { get; set; } = true;

    /// <summary>Sticky window geometry (DIPs); null = center on first open.</summary>
    public double? StickyLeft { get; set; }
    public double? StickyTop { get; set; }
    public double StickyWidth { get; set; } = 340;
    public double StickyHeight { get; set; } = 520;

    // ─── Sync (multi-device, self-hosted server) ───────────
    public bool SyncEnabled { get; set; }
    public string SyncServerUrl { get; set; } = "";
    public string SyncKey { get; set; } = "";
    /// <summary>Per-device id, auto-generated on first run (the server treats each device independently).</summary>
    public string DeviceId { get; set; } = "";
    /// <summary>High-water mark of server changes applied on this device; 0 = never synced.</summary>
    public long LastSyncServerSeq { get; set; }
    public long LastSyncTime { get; set; }

    /// <summary>Auto-update feeds, tried in order. Empty = default (GitHub).</summary>
    public List<UpdateSourceSetting> UpdateSources { get; set; } = new();

    /// <summary>Temp copy of a restored backup; swapped over DbPath on next startup, then cleared.</summary>
    public string? PendingRestorePath { get; set; }
}

public class UpdateSourceSetting
{
    /// <summary>"github" / "gitee" (JSON releases API) or "appcast" (AutoUpdater.NET XML).</summary>
    public string Type { get; set; } = "github";
    public string Url { get; set; } = "";
}

public static class SettingsService
{
    /// <summary>Settings directory; tests repoint this at a temp dir via <see cref="UseDirectory"/>.</summary>
    internal static string SettingsDir { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToDo");
    private static string SettingsFile => Path.Combine(SettingsDir, "settings.json");

    public static string DefaultDbPath => Path.Combine(SettingsDir, "todo.db");

    /// <summary>Where a chosen backup is staged before it replaces the live DB on restart.</summary>
    public static string PendingRestoreFilePath => Path.Combine(SettingsDir, "pending-restore.db");

    private const int CurrentSchemaVersion = 6;   // v6 adds ShowTask* row toggles; v5 adds ReminderSoundPath; v4 adds StickyShowTags; v3 added the Behavior block + sticky geometry

    private static AppSettings? _current;

    /// <summary>Raised after settings are persisted, so live UI (e.g. the main task list)
    /// can re-read toggles that drive bindings.</summary>
    public static event Action? SettingsChanged;
    public static List<UpdateSourceSetting> DefaultUpdateSources { get; } = new()
    {
        new() { Type = "github", Url = "https://api.github.com/repos/LinBanfang/ToDo/releases/latest" },
        new() { Type = "gitee", Url = "https://gitee.com/api/v5/repos/wu-bin-921/ToDo/releases/latest" },
    };

    public static AppSettings Current
    {
        get
        {
            if (_current == null) Load();
            return _current!;
        }
    }

    /// <summary>Test seam: point settings at an isolated directory and reset cached state.</summary>
    internal static void UseDirectory(string dir)
    {
        _current = null;
        SettingsDir = dir;
    }

    public static void Load()
    {
        Directory.CreateDirectory(SettingsDir);
        var isNewFile = !File.Exists(SettingsFile);
        if (!isNewFile)
        {
            try
            {
                var json = File.ReadAllText(SettingsFile);
                _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch { _current = new AppSettings(); }
        }
        else
        {
            _current = new AppSettings();
        }

        if (string.IsNullOrEmpty(_current.DbPath))
            _current.DbPath = DefaultDbPath;

        if (isNewFile)
        {
            // First launch: prefill the default update sources
            _current.UpdateSources = new List<UpdateSourceSetting>(DefaultUpdateSources);
        }

        // Legacy files (no SchemaVersion) keep the property defaults for fields
        // added later, so stamping the version + saving is all the migration needs.
        if (isNewFile || _current.SchemaVersion < CurrentSchemaVersion)
        {
            _current.SchemaVersion = CurrentSchemaVersion;
            Save();
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFile, json);
        SettingsChanged?.Invoke();
    }

    public static void SetDbPath(string newPath)
    {
        var oldPath = Current.DbPath;
        if (oldPath == newPath) return;

        // Migrate data: copy existing DB to new location
        if (File.Exists(oldPath) && !File.Exists(newPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
            File.Copy(oldPath, newPath);
        }

        Current.DbPath = newPath;
        Save();
    }
}
