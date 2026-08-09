using ToDo.Services;

namespace ToDo.ViewModels;

/// <summary>行为：托盘常驻 + 迷你便笺相关开关（即时生效）。</summary>
public sealed class BehaviorSection : SettingsSection
{
    private bool _minimizeToTrayOnClose;
    private bool _stickyShowTags;
    private bool _showTaskTags;
    private bool _showTaskSteps;
    private bool _showTaskDue;
    private bool _showTaskReminder;
    private bool _showTaskNote;

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

    public bool ShowTaskTags
    {
        get => _showTaskTags;
        set
        {
            if (SetProperty(ref _showTaskTags, value))
            {
                SettingsService.Current.ShowTaskTags = value;
                SettingsService.Save();
            }
        }
    }

    public bool ShowTaskSteps
    {
        get => _showTaskSteps;
        set
        {
            if (SetProperty(ref _showTaskSteps, value))
            {
                SettingsService.Current.ShowTaskSteps = value;
                SettingsService.Save();
            }
        }
    }

    public bool ShowTaskDue
    {
        get => _showTaskDue;
        set
        {
            if (SetProperty(ref _showTaskDue, value))
            {
                SettingsService.Current.ShowTaskDue = value;
                SettingsService.Save();
            }
        }
    }

    public bool ShowTaskReminder
    {
        get => _showTaskReminder;
        set
        {
            if (SetProperty(ref _showTaskReminder, value))
            {
                SettingsService.Current.ShowTaskReminder = value;
                SettingsService.Save();
            }
        }
    }

    public bool ShowTaskNote
    {
        get => _showTaskNote;
        set
        {
            if (SetProperty(ref _showTaskNote, value))
            {
                SettingsService.Current.ShowTaskNote = value;
                SettingsService.Save();
            }
        }
    }

    public BehaviorSection()
    {
        _minimizeToTrayOnClose = SettingsService.Current.MinimizeToTrayOnClose;
        _stickyShowTags = SettingsService.Current.StickyShowTags;
        _showTaskTags = SettingsService.Current.ShowTaskTags;
        _showTaskSteps = SettingsService.Current.ShowTaskSteps;
        _showTaskDue = SettingsService.Current.ShowTaskDue;
        _showTaskReminder = SettingsService.Current.ShowTaskReminder;
        _showTaskNote = SettingsService.Current.ShowTaskNote;
    }
}
