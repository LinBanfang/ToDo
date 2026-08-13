using System;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using ToDo.Services;

namespace ToDo.ViewModels;

/// <summary>
/// 同步：开关、服务器地址、同步密钥、设备 ID、实时状态与"立即同步"。
/// App.Sync is created before the ViewModel tree, so this section can subscribe to its
/// StatusChanged and stay live while the settings page is open.
/// </summary>
public sealed partial class SyncSection : SettingsSection
{
    private bool _enabled;
    private string _serverUrl;
    private string _key;

    public bool SyncEnabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                SettingsService.Current.SyncEnabled = value;
                SettingsService.Save();
                // Turning sync on kicks a round-trip; off just re-evaluates the status.
                if (value) App.Sync?.Trigger();
                else App.Sync?.RefreshStatus();
            }
        }
    }

    public string SyncServerUrl
    {
        get => _serverUrl;
        set
        {
            if (SetProperty(ref _serverUrl, value))
            {
                SettingsService.Current.SyncServerUrl = value;
                SettingsService.Save();
                OnPropertyChanged(nameof(SyncInsecureUrlVisible));
            }
        }
    }

    /// <summary>True when the server URL is set but not HTTPS — the sync key and all
    /// data would travel the wire in plaintext. Drives the warning under the URL field.</summary>
    public bool SyncInsecureUrlVisible =>
        !string.IsNullOrWhiteSpace(SyncServerUrl) &&
        !SyncServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    public string SyncKey
    {
        get => _key;
        set
        {
            if (SetProperty(ref _key, value))
            {
                SettingsService.Current.SyncKey = value;
                SettingsService.Save();
            }
        }
    }

    public string DeviceId => SettingsService.Current.DeviceId;

    // Same neutral gray as FluentColors' TextDisabledBrush — only used before App.Sync exists.
    private static readonly Brush _idleBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0x9F, 0x9D));

    public string StatusText => App.Sync?.StatusText ?? Loc.SyncStatusDisabled;
    public string LastSyncText => App.Sync?.LastSyncText ?? "";
    public Brush StatusBrush => App.Sync?.StatusBrush ?? _idleBrush;

    public SyncSection()
    {
        _enabled = SettingsService.Current.SyncEnabled;
        _serverUrl = SettingsService.Current.SyncServerUrl;
        _key = SettingsService.Current.SyncKey;
        if (App.Sync != null) App.Sync.StatusChanged += OnSyncStatusChanged;
    }

    private void OnSyncStatusChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LastSyncText));
        OnPropertyChanged(nameof(StatusBrush));
    }

    [RelayCommand]
    private void SyncNow()
    {
        SettingsService.Save();   // flush any in-flight textbox edits
        App.Sync?.Trigger();
    }
}
