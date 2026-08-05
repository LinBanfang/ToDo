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
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToDo");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public static string DefaultDbPath => Path.Combine(SettingsDir, "todo.db");

    /// <summary>Where a chosen backup is staged before it replaces the live DB on restart.</summary>
    public static string PendingRestoreFilePath => Path.Combine(SettingsDir, "pending-restore.db");

    private const int CurrentSchemaVersion = 1;

    private static AppSettings? _current;

    /// <summary>Auto-update feeds written on first launch and used when settings has none.</summary>
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
