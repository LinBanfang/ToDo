using System.IO;
using System.Text.Json;

namespace ToDo.Services;

public class AppSettings
{
    public string DbPath { get; set; } = "";
    public string Theme { get; set; } = "Light"; // Light, Dark
}

public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToDo");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public static string DefaultDbPath => Path.Combine(SettingsDir, "todo.db");

    private static AppSettings? _current;
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
        if (File.Exists(SettingsFile))
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
