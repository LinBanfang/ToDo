using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ToDo.Services;
using ToDo.Views.Dialogs;

namespace ToDo.ViewModels;

/// <summary>
/// Root VM for the in-app settings page. Holds the ordered nav sections;
/// each section is a concrete type so the page can pick its content template by type.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    public ObservableCollection<SettingsSection> Sections { get; } = new();

    [ObservableProperty]
    private SettingsSection? _selectedSection;

    public SettingsViewModel()
    {
        Sections.Add(new GeneralSection { Key = "general", Title = Loc.General });
        Sections.Add(new AppearanceSection { Key = "appearance", Title = Loc.Appearance });
        Sections.Add(new DataSection { Key = "data", Title = Loc.Data });
        Sections.Add(new SyncSection { Key = "sync", Title = Loc.SyncSectionTitle });
        Sections.Add(new UpdateSection { Key = "update", Title = Loc.Updates });
        Sections.Add(new ReminderSection { Key = "reminder", Title = Loc.RemindersSection });
        Sections.Add(new BehaviorSection { Key = "behavior", Title = Loc.Behavior });
        Sections.Add(new AboutSection { Key = "about", Title = Loc.About });
        SelectedSection = Sections[0];
    }
}

/// <summary>Base for settings-page sections: nav model + content-template key.</summary>
public abstract class SettingsSection : ObservableObject
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
}

/// <summary>常规：语言（重启生效）。</summary>
public sealed class GeneralSection : SettingsSection
{
    private string _language;

    public string Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
                SettingsService.Current.Language = value;
                SettingsService.Save();
                OnPropertyChanged(nameof(LanguageHint));
            }
        }
    }

    /// <summary>Show a "restart to apply" hint when the picked language isn't the live one yet.</summary>
    public string LanguageHint =>
        Language == (Loc.Language == AppLanguage.Chinese ? "Chinese" : "English")
            ? "" : Loc.RestartToApply;

    public GeneralSection() => _language = SettingsService.Current.Language;
}

/// <summary>外观：主题（即时生效）。</summary>
public sealed class AppearanceSection : SettingsSection
{
    private string _theme;

    public string Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                SettingsService.Current.Theme = value;
                SettingsService.Save();
                ThemeService.Apply(value);
            }
        }
    }

    public AppearanceSection() => _theme = SettingsService.Current.Theme;
}

/// <summary>数据：数据库路径、备份导出/恢复。</summary>
public partial class DataSection : SettingsSection
{
    /// <summary>Display the DB path with the user-specific prefix replaced by its
    /// environment-variable form (C:\Users\Alice\AppData\Local → %LOCALAPPDATA%),
    /// so the path shown in the UI (and README screenshots) never leaks a username.
    /// Display only — the change-dialog and storage always use the real path.</summary>
    public string DbPath => MaskPath(App.Database?.StoragePath ?? SettingsService.Current.DbPath);

    private static string MaskPath(string path)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (localAppData.Length > 0 && path.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase))
            return "%LOCALAPPDATA%" + path.Substring(localAppData.Length);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 0 && path.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
            return "%USERPROFILE%" + path.Substring(profile.Length);
        return path;
    }

    [RelayCommand]
    private void ChangeDbPath()
    {
        var currentPath = App.Database?.StoragePath ?? SettingsService.Current.DbPath;
        var dialog = new Views.Dialogs.DbPathDialog(currentPath) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() == true && dialog.ResultPath != currentPath)
        {
            try
            {
                SettingsService.SetDbPath(dialog.ResultPath);
                FluentDialog.Show(Application.Current.MainWindow, Loc.DbPathChanged, "To Do", MsgKind.Info);
                OnPropertyChanged(nameof(DbPath));
            }
            catch (Exception ex)
            {
                FluentDialog.Show(Application.Current.MainWindow, ex.Message, Loc.Error, MsgKind.Error);
            }
        }
    }

    [RelayCommand]
    private void ExportBackup()
    {
        if (App.Database == null) return;
        var dialog = new SaveFileDialog
        {
            Title = Loc.ExportBackup,
            FileName = $"todo-backup-{DateTime.Now:yyyyMMdd}.db",
            Filter = Loc.BackupFileFilter,
            DefaultExt = "db"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                App.Database.ExportTo(dialog.FileName);
                FluentDialog.Show(Application.Current.MainWindow, Loc.BackupSaved(dialog.FileName), Loc.ExportBackup, MsgKind.Info);
            }
            catch (Exception ex)
            {
                FluentDialog.Show(Application.Current.MainWindow, ex.Message, Loc.Error, MsgKind.Error);
            }
        }
    }

    [RelayCommand]
    private void RestoreBackup()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.SelectBackupFile,
            Filter = Loc.BackupFileFilter,
            DefaultExt = "db"
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.Copy(dialog.FileName, SettingsService.PendingRestoreFilePath, overwrite: true);
                SettingsService.Current.PendingRestorePath = SettingsService.PendingRestoreFilePath;
                SettingsService.Save();
                FluentDialog.Show(Application.Current.MainWindow, Loc.RestoreStaged, Loc.RestoreBackup, MsgKind.Info);
            }
            catch (Exception ex)
            {
                FluentDialog.Show(Application.Current.MainWindow, ex.Message, Loc.Error, MsgKind.Error);
            }
        }
    }
}

/// <summary>更新：启动检查开关、更新源列表编辑、立即检查。</summary>
public partial class UpdateSection : SettingsSection
{
    public ObservableCollection<UpdateSourceRow> UpdateSources { get; } = new();

    private bool _checkForUpdatesOnStartup;

    public bool CheckForUpdatesOnStartup
    {
        get => _checkForUpdatesOnStartup;
        set
        {
            if (SetProperty(ref _checkForUpdatesOnStartup, value))
            {
                SettingsService.Current.CheckForUpdatesOnStartup = value;
                SettingsService.Save();
            }
        }
    }

    public UpdateSection()
    {
        _checkForUpdatesOnStartup = SettingsService.Current.CheckForUpdatesOnStartup;
        var configured = SettingsService.Current.UpdateSources;
        var list = configured.Count > 0 ? configured : SettingsService.DefaultUpdateSources;
        foreach (var s in list)
        {
            var row = new UpdateSourceRow { Type = s.Type, Url = s.Url };
            row.Changed += PersistSources;
            UpdateSources.Add(row);
        }
    }

    private void PersistSources()
    {
        SettingsService.Current.UpdateSources = UpdateSources
            .Where(r => !string.IsNullOrWhiteSpace(r.Url))
            .Select(r => new UpdateSourceSetting { Type = r.Type, Url = r.Url.Trim() })
            .ToList();
        SettingsService.Save();
    }

    [RelayCommand]
    private void AddSource()
    {
        var row = new UpdateSourceRow { Type = "github", Url = "" };
        row.Changed += PersistSources;
        UpdateSources.Add(row);
    }

    [RelayCommand]
    private void RemoveSource(UpdateSourceRow row)
    {
        row.Changed -= PersistSources;
        UpdateSources.Remove(row);
        PersistSources();
    }

    [RelayCommand]
    private void CheckNow()
    {
        PersistSources();
        UpdateService.CheckForUpdatesNow();
    }
}

/// <summary>One editable update-source row in the sources list.</summary>
public sealed class UpdateSourceRow : ObservableObject
{
    private string _type = "github";
    private string _url = "";

    public string Type
    {
        get => _type;
        set { if (SetProperty(ref _type, value)) Changed?.Invoke(); }
    }

    public string Url
    {
        get => _url;
        set { if (SetProperty(ref _url, value)) Changed?.Invoke(); }
    }

    /// <summary>Fired on any edit so the section can persist the sources list.</summary>
    public event Action? Changed;
}

/// <summary>提醒：通知 + 提示音开关 + 铃声试听/选择（轮询即时生效）。</summary>
public sealed partial class ReminderSection : SettingsSection
{
    private bool _notifications;
    private bool _sound;

    public bool ReminderNotifications
    {
        get => _notifications;
        set
        {
            if (SetProperty(ref _notifications, value))
            {
                SettingsService.Current.ReminderNotifications = value;
                SettingsService.Save();
            }
        }
    }

    public bool ReminderSound
    {
        get => _sound;
        set
        {
            if (SetProperty(ref _sound, value))
            {
                SettingsService.Current.ReminderSound = value;
                SettingsService.Save();
            }
        }
    }

    /// <summary>当前铃声的显示名：自定义文件的文件名，未设置时显示系统提示音。</summary>
    public string SoundLabel
    {
        get
        {
            var path = SettingsService.Current.ReminderSoundPath;
            if (string.IsNullOrWhiteSpace(path)) return Loc.DefaultRingtone;
            var name = Path.GetFileName(path);
            return File.Exists(path) ? name : $"{name} ({Loc.SoundMissing})";
        }
    }

    /// <summary>是否已配置自定义铃声（控制"重置"按钮是否显示）。</summary>
    public bool HasCustomSound => !string.IsNullOrWhiteSpace(SettingsService.Current.ReminderSoundPath);

    [RelayCommand]
    private void TestSound() => ReminderSoundPlayer.Play();

    [RelayCommand]
    private void ChooseSound()
    {
        var dialog = new OpenFileDialog
        {
            Title = Loc.ChooseReminderSound,
            Filter = Loc.SoundFileFilter,
            DefaultExt = "wav",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            SettingsService.Current.ReminderSoundPath = dialog.FileName;
            SettingsService.Save();
            OnPropertyChanged(nameof(SoundLabel));
            OnPropertyChanged(nameof(HasCustomSound));
        }
    }

    [RelayCommand]
    private void ResetSound()
    {
        SettingsService.Current.ReminderSoundPath = "";
        SettingsService.Save();
        OnPropertyChanged(nameof(SoundLabel));
        OnPropertyChanged(nameof(HasCustomSound));
    }

    public ReminderSection()
    {
        _notifications = SettingsService.Current.ReminderNotifications;
        _sound = SettingsService.Current.ReminderSound;
    }
}

/// <summary>关于：应用名称、版本、简介、项目主页与第三方组件许可。</summary>
public sealed class AboutSection : SettingsSection
{
    public string Version { get; }
    public string VersionText => $"{Loc.VersionLabel} {Version}";
    public string Copyright => "© 2026 LinBanfang · MIT License";
    public string GitHubUrl => "https://github.com/LinBanfang/ToDo";
    public string GiteeUrl => "https://gitee.com/wu-bin-921/ToDo";
    public ImageSource? Icon { get; } = LoadAppIcon();

    public AboutSection()
    {
        // 从程序集读取版本（1.0.13.0 → 1.0.13），避免与 csproj 版本号脱节
        var v = typeof(AboutSection).Assembly.GetName().Version;
        Version = v == null ? "0.0.0" : v.ToString(3);
    }

    /// <summary>加载应用图标并取最大帧，保证 48px 显示在高 DPI 下依然清晰。</summary>
    private static ImageSource? LoadAppIcon()
    {
        try
        {
            var decoder = new IconBitmapDecoder(
                new Uri("pack://application:,,,/Resources/app.ico"),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            return decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).FirstOrDefault();
        }
        catch
        {
            return null; // 图标加载失败不应影响设置页
        }
    }
}
