using ToDo.Services;

namespace ToDo.ViewModels;

/// <summary>行为：托盘常驻 + 迷你便笺相关开关（即时生效）。</summary>
public sealed class BehaviorSection : SettingsSection
{
    private bool _minimizeToTrayOnClose;
    private bool _stickyShowTags;

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (SetProperty(ref _minimizeToTrayOnClose, value))
            {
                SettingsService.Current.MinimizeToTrayOnClose = value;
                SettingsService.Save();
            }
        }
    }

    /// <summary>便笺任务行是否显示彩色标签 pill。</summary>
    public bool StickyShowTags
    {
        get => _stickyShowTags;
        set
        {
            if (SetProperty(ref _stickyShowTags, value))
            {
                SettingsService.Current.StickyShowTags = value;
                SettingsService.Save();
            }
        }
    }

    public BehaviorSection()
    {
        _minimizeToTrayOnClose = SettingsService.Current.MinimizeToTrayOnClose;
        _stickyShowTags = SettingsService.Current.StickyShowTags;
    }
}
