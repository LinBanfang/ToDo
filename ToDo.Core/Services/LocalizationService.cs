using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ToDo.Services;

public enum AppLanguage { English, Chinese }

public static class Loc
{
    public static AppLanguage Language { get; private set; } = AppLanguage.Chinese;
    public static event Action? LanguageChanged;

    public static void SetLanguage(AppLanguage lang)
    {
        if (Language != lang)
        {
            Language = lang;
            LanguageChanged?.Invoke();
        }
    }

    public static void Toggle()
    {
        SetLanguage(Language == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese);
    }

    // ─── String properties ────────────────────────────────
    public static string AppTitle => Language == AppLanguage.Chinese ? "待办事项" : "To Do";
    public static string Search => Language == AppLanguage.Chinese ? "搜索" : "Search";
    public static string SearchResults => Language == AppLanguage.Chinese ? "搜索结果" : "Search Results";
    public static string System => Language == AppLanguage.Chinese ? "系统" : "SYSTEM";
    public static string Lists => Language == AppLanguage.Chinese ? "列表" : "LISTS";
    public static string Tags => Language == AppLanguage.Chinese ? "标签" : "TAGS";
    public static string AddTask => Language == AppLanguage.Chinese ? "添加任务" : "Add a task";
    public static string NewList => Language == AppLanguage.Chinese ? "+ 新建列表" : "+ New list";
    public static string NewListText => Language == AppLanguage.Chinese ? "新建列表" : "New list";
    public static string NewGroup => Language == AppLanguage.Chinese ? "新建分组" : "New group";
    public static string NewListGroup => Language == AppLanguage.Chinese ? "新建列表分组" : "New list group";
    public static string DeleteListGroup => Language == AppLanguage.Chinese ? "删除列表分组" : "Delete list group";
    public static string ConfirmDeleteListGroupMsg(string name) =>
        Language == AppLanguage.Chinese ? $"确定删除分组 \"{name}\" 吗？组内列表将变为未分组。" : $"Delete group \"{name}\"? Lists will become ungrouped.";
    public static string TaskDetails => Language == AppLanguage.Chinese ? "任务详情" : "Task Details";
    public static string Steps => Language == AppLanguage.Chinese ? "步骤" : "STEPS";
    public static string AddStep => Language == AppLanguage.Chinese ? "添加步骤" : "Add a step";
    public static string Closed => Language == AppLanguage.Chinese ? "已关闭" : "CLOSED";
    public static string Completed => Language == AppLanguage.Chinese ? "已完成" : "Completed";
    public static string Cancelled => Language == AppLanguage.Chinese ? "已取消" : "Cancelled";
    public static string EditCloseTime => Language == AppLanguage.Chinese ? "编辑关闭时间..." : "Edit close time...";
    public static string Notes => Language == AppLanguage.Chinese ? "备注" : "NOTES";
    public static string Attachments => Language == AppLanguage.Chinese ? "附件" : "ATTACHMENTS";
    public static string AddAttachment => Language == AppLanguage.Chinese ? "添加附件" : "Add attachment";
    public static string NoAttachments => Language == AppLanguage.Chinese ? "暂无附件" : "No attachments";
    public static string AttachmentOpenFailed(string name) => Language == AppLanguage.Chinese
        ? $"无法打开附件 \"{name}\"" : $"Couldn't open attachment \"{name}\"";
    public static string AttachmentTooLarge(int mb) => Language == AppLanguage.Chinese
        ? $"单个附件不能超过 {mb} MB" : $"Attachment too large (max {mb} MB)";
    public static string Delete => Language == AppLanguage.Chinese ? "删除" : "Delete";
    public static string Complete => Language == AppLanguage.Chinese ? "完成" : "Complete";
    public static string Cancel => Language == AppLanguage.Chinese ? "取消" : "Cancel";
    public static string OK => Language == AppLanguage.Chinese ? "确定" : "OK";
    public static string AddToMyDay => Language == AppLanguage.Chinese ? "添加到我的一天" : "Add to My Day";
    public static string RemoveFromMyDay => Language == AppLanguage.Chinese ? "从我的一天移除" : "Remove from My Day";
    public static string MarkImportant => Language == AppLanguage.Chinese ? "标记为重要" : "Mark as important";
    public static string RemoveImportance => Language == AppLanguage.Chinese ? "取消重要标记" : "Remove importance";
    public static string MoveToList => Language == AppLanguage.Chinese ? "移动到列表" : "Move to list";
    public static string MoveToGroup => Language == AppLanguage.Chinese ? "移动到分组" : "Move to group";
    public static string Ungrouped => Language == AppLanguage.Chinese ? "未分组" : "Ungrouped";
    public static string DropToUngroupHint => Language == AppLanguage.Chinese ? "拖到此处移出分组" : "Drop here to ungroup";
    public static string RemoveFromGroup => Language == AppLanguage.Chinese ? "从分组移出" : "Remove from group";
    public static string ReopenTask => Language == AppLanguage.Chinese ? "重新打开" : "Reopen task";
    public static string DeleteTask => Language == AppLanguage.Chinese ? "删除任务" : "Delete task";
    public static string DeleteGroup => Language == AppLanguage.Chinese ? "删除分组" : "Delete group";
    public static string Rename => Language == AppLanguage.Chinese ? "重命名" : "Rename";
    public static string Group => Language == AppLanguage.Chinese ? "分组" : "Group";
    public static string RenameList => Language == AppLanguage.Chinese ? "重命名列表" : "Rename list";
    public static string DeleteList => Language == AppLanguage.Chinese ? "删除列表" : "Delete list";
    public static string ManageTags => Language == AppLanguage.Chinese ? "管理标签" : "Manage Tags";
    public static string AddTag => Language == AppLanguage.Chinese ? "添加标签" : "Add tag";
    public static string NoTags => Language == AppLanguage.Chinese ? "暂无标签" : "No tags";
    public static string ConfirmDelete => Language == AppLanguage.Chinese ? "确认删除" : "Delete";
    public static string ConfirmDeleteMsg(string name) =>
        Language == AppLanguage.Chinese ? $"确定删除 \"{name}\" 吗？" : $"Delete \"{name}\"?";
    public static string TagNameExists(string name) =>
        Language == AppLanguage.Chinese ? $"标签名 \"{name}\" 已存在" : $"A tag named \"{name}\" already exists";
    public static string ConfirmDeleteGroupMsg(string name) =>
        Language == AppLanguage.Chinese ? $"确定删除分组 \"{name}\" 吗？任务将变为未分组。" : $"Delete group \"{name}\"? Tasks will become ungrouped.";
    public static string CompletedSection => Language == AppLanguage.Chinese ? "已完成" : "Completed";
    public static string EditTime => Language == AppLanguage.Chinese ? "编辑关闭时间" : "Edit Close Time";
    public static string Date => Language == AppLanguage.Chinese ? "日期" : "Date";
    public static string Time => Language == AppLanguage.Chinese ? "时间" : "Time";
    public static string Save => Language == AppLanguage.Chinese ? "保存" : "Save";
    public static string TagPlaceholder => Language == AppLanguage.Chinese ? "新标签名" : "New tag name";
    public static string Add => Language == AppLanguage.Chinese ? "添加" : "Add";
    public static string Done => Language == AppLanguage.Chinese ? "完成" : "Done";
    public static string Today => Language == AppLanguage.Chinese ? "今天" : "Today";
    public static string Tomorrow => Language == AppLanguage.Chinese ? "明天" : "Tomorrow";
    public static string ThisWeek => Language == AppLanguage.Chinese ? "下周" : "Next Week";
    public static string Later => Language == AppLanguage.Chinese ? "稍后" : "Later";
    public static string PickDate => Language == AppLanguage.Chinese ? "选择日期..." : "Pick a date...";
    public static string NoDueDate => Language == AppLanguage.Chinese ? "无截止日期" : "No Due Date";

    // System list display names
    public static string MyDay => Language == AppLanguage.Chinese ? "我的一天" : "My Day";
    public static string Important => Language == AppLanguage.Chinese ? "重要" : "Important";
    public static string Planned => Language == AppLanguage.Chinese ? "计划内" : "Planned";
    public static string Tasks => Language == AppLanguage.Chinese ? "任务" : "Tasks";
    public static string DbPathTitle => Language == AppLanguage.Chinese ? "数据库路径" : "Database Path";
    public static string DbPathPrompt => Language == AppLanguage.Chinese ? "请输入数据库文件的完整路径。修改后当前数据将自动迁移到新位置。" : "Enter the full path for the database file. Existing data will be migrated to the new location.";
    public static string DbPathChanged => Language == AppLanguage.Chinese ? "路径已更改。请重启应用以使用新数据库位置。" : "Path changed. Please restart the app to use the new database location.";
    public static string SelectDbFile => Language == AppLanguage.Chinese ? "选择数据库文件" : "Select database file";
    public static string DbFileFilter => Language == AppLanguage.Chinese
        ? "数据库文件 (*.db)|*.db|所有文件 (*.*)|*.*"
        : "Database files (*.db)|*.db|All files (*.*)|*.*";
    public static string InvalidPathMsg => Language == AppLanguage.Chinese ? "请输入有效路径。" : "Please enter a valid path.";
    public static string Error => Language == AppLanguage.Chinese ? "错误" : "Error";
    public static string Yesterday => Language == AppLanguage.Chinese ? "昨天" : "Yesterday";
    public static string NewListName => Language == AppLanguage.Chinese ? "新列表" : "New list";
    public static string MarkIncomplete => Language == AppLanguage.Chinese ? "标记为未完成" : "Mark incomplete";
    public static string PromoteToTask => Language == AppLanguage.Chinese ? "升级为任务" : "Promote to task";

    // Relative time (TimestampToRelativeStringConverter)
    public static string JustNow => Language == AppLanguage.Chinese ? "刚刚" : "just now";
    public static string MinutesAgo(int n) => Language == AppLanguage.Chinese ? $"{n} 分钟前" : $"{n}m ago";
    public static string HoursAgo(int n) => Language == AppLanguage.Chinese ? $"{n} 小时前" : $"{n}h ago";
    public static string DaysAgo(int n) => Language == AppLanguage.Chinese ? $"{n} 天前" : $"{n}d ago";
    // English date strings use InvariantCulture so they stay English regardless of
    // the OS culture (a zh-CN system must not render "Mar 5, 2024" as "3月 5, 2024").
    public static string RelativeDate(DateTime dt) =>
        Language == AppLanguage.Chinese ? $"{dt.Year}年{dt.Month}月{dt.Day}日" : dt.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    // Short date (DueDateToStringConverter)
    public static string ShortDate(DateTime dt) =>
        Language == AppLanguage.Chinese ? $"{dt.Month}月{dt.Day}日" : dt.ToString("MMM d", CultureInfo.InvariantCulture);
    public static string ReminderTime(DateTime dt) =>
        Language == AppLanguage.Chinese ? $"{dt.Month}月{dt.Day}日 {dt:HH:mm}" : dt.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture);
    // A reminder falling today shows just the time on the task row.
    public static string ReminderTimeOnly(DateTime dt) => dt.ToString("HH:mm", CultureInfo.InvariantCulture);
    // A reminder on another day shows just the date on the task row (no clock time).
    public static string ReminderDateOnly(DateTime dt) =>
        Language == AppLanguage.Chinese ? $"{dt.Month}月{dt.Day}日" : dt.ToString("MMM d", CultureInfo.InvariantCulture);
    public static string Reminder => Language == AppLanguage.Chinese ? "提醒" : "Reminder";
    public static string AddDueDate => Language == AppLanguage.Chinese ? "添加截止日期" : "Add due date";
    public static string AddReminder => Language == AppLanguage.Chinese ? "添加提醒" : "Add reminder";
    public static string UpdateAvailable => Language == AppLanguage.Chinese ? "发现新版本" : "Update available";
    public static string DownloadUpdate => Language == AppLanguage.Chinese ? "立即更新" : "Update now";
    public static string RemindLater => Language == AppLanguage.Chinese ? "以后再说" : "Remind me later";
    public static string SkipVersion => Language == AppLanguage.Chinese ? "跳过此版本" : "Skip this version";
    public static string UpdateDownloaded => Language == AppLanguage.Chinese ? "已下载到" : "Downloaded to";
    // latest is guaranteed non-empty here: AutoUpdater throws MissingFieldException
    // (→ the failure branch) before a "no update" result can reach the UI.
    public static string UpdateUpToDate(string latest) =>
        Language == AppLanguage.Chinese
            ? $"已是最新版本（最新版本 {latest}）"
            : $"You're up to date (latest version {latest})";
    public static string UpdateSourceNoInfo => Language == AppLanguage.Chinese
        ? "无法从更新源获取版本信息，请检查网络或更新源配置"
        : "Couldn't get version info from any update source; check your network or source configuration";
    public static string UpdateCheckFailed(string detail) =>
        Language == AppLanguage.Chinese ? $"检查更新失败：{detail}" : $"Update check failed: {detail}";

    // Settings page
    public static string Settings => Language == AppLanguage.Chinese ? "设置" : "Settings";
    public static string Back => Language == AppLanguage.Chinese ? "返回" : "Back";
    public static string General => Language == AppLanguage.Chinese ? "常规" : "General";
    public static string Appearance => Language == AppLanguage.Chinese ? "外观" : "Appearance";
    public static string Data => Language == AppLanguage.Chinese ? "数据" : "Data";
    public static string Updates => Language == AppLanguage.Chinese ? "更新" : "Updates";
    public static string RemindersSection => Language == AppLanguage.Chinese ? "提醒" : "Reminders";
    public static string LanguageName => Language == AppLanguage.Chinese ? "语言" : "Language";
    public static string Theme => Language == AppLanguage.Chinese ? "主题" : "Theme";
    public static string LightTheme => Language == AppLanguage.Chinese ? "浅色" : "Light";
    public static string DarkTheme => Language == AppLanguage.Chinese ? "深色" : "Dark";
    public static string RestartToApply => Language == AppLanguage.Chinese ? "重启后生效" : "Takes effect after restart";
    public static string AppliesImmediately => Language == AppLanguage.Chinese ? "即时生效" : "Applies immediately";
    public static string Change => Language == AppLanguage.Chinese ? "更改" : "Change";
    public static string ExportBackup => Language == AppLanguage.Chinese ? "导出备份" : "Export backup";
    public static string RestoreBackup => Language == AppLanguage.Chinese ? "从备份恢复" : "Restore from backup";
    public static string BackupSaved(string path) =>
        Language == AppLanguage.Chinese ? $"备份已导出到：{path}" : $"Backup exported to: {path}";
    public static string RestoreStaged => Language == AppLanguage.Chinese
        ? "备份已暂存，将在下次启动时替换当前数据。" : "Backup staged. It will replace the current data on next startup.";
    public static string SelectBackupFile => Language == AppLanguage.Chinese ? "选择备份文件" : "Select backup file";
    public static string BackupFileFilter => Language == AppLanguage.Chinese
        ? "数据库备份 (*.db)|*.db|所有文件 (*.*)|*.*"
        : "Database backup (*.db)|*.db|All files (*.*)|*.*";
    public static string CheckForUpdatesOnStartup => Language == AppLanguage.Chinese ? "启动时检查更新" : "Check for updates on startup";
    public static string UpdateSources => Language == AppLanguage.Chinese ? "更新源" : "Update sources";
    public static string AddSource => Language == AppLanguage.Chinese ? "添加源" : "Add source";
    public static string RemoveSource => Language == AppLanguage.Chinese ? "移除" : "Remove";
    public static string CheckUpdatesNow => Language == AppLanguage.Chinese ? "立即检查更新" : "Check for updates now";
    public static string EnableReminderNotifications => Language == AppLanguage.Chinese ? "启用提醒通知" : "Enable reminder notifications";
    public static string Apply => Language == AppLanguage.Chinese ? "应用" : "Apply";
    public static string MoreColors => Language == AppLanguage.Chinese ? "更多颜色…" : "More colors…";
    // List theme dialog (ADR-014)
    public static string ListTheme => Language == AppLanguage.Chinese ? "列表主题" : "List theme";
    public static string NoBackground => Language == AppLanguage.Chinese ? "无背景" : "No background";
    public static string SolidColor => Language == AppLanguage.Chinese ? "纯色" : "Solid color";
    public static string ChooseImage => Language == AppLanguage.Chinese ? "选择图片…" : "Choose image…";
    public static string RemoveImage => Language == AppLanguage.Chinese ? "移除图片" : "Remove image";
    public static string ImageTooLarge(int mb) =>
        Language == AppLanguage.Chinese ? $"图片不能超过 {mb} MB" : $"Image too large (max {mb} MB)";
    public static string ImageFileFilter => Language == AppLanguage.Chinese
        ? "图片文件 (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件 (*.*)|*.*"
        : "Images (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files (*.*)|*.*";
    public static string ListThemeImageLocalHint => Language == AppLanguage.Chinese
        ? "背景图片仅保存在本设备，不会同步。"
        : "Background images stay on this device and won't sync.";
    public static string BackgroundStrength => Language == AppLanguage.Chinese
        ? "背景强弱" : "Background strength";
    public static string BackgroundStrengthLocalHint => Language == AppLanguage.Chinese
        ? "背景强弱仅本设备生效，不同步。"
        : "Background strength is local to this device and won't sync.";
    public static string CardOpacity => Language == AppLanguage.Chinese
        ? "卡片不透明度" : "Card opacity";
    public static string CardOpacityLocalHint => Language == AppLanguage.Chinese
        ? "卡片不透明度仅本设备生效，不同步；越高越不透明，越能盖住背景。"
        : "Card opacity is local to this device and won't sync; higher is more solid.";
    public static string PlayReminderSound => Language == AppLanguage.Chinese ? "播放提示音" : "Play reminder sound";
    public static string ReminderRingtone => Language == AppLanguage.Chinese ? "提醒铃声" : "Reminder ringtone";
    public static string DefaultRingtone => Language == AppLanguage.Chinese ? "默认铃声" : "Default ringtone";
    public static string TestSound => Language == AppLanguage.Chinese ? "试听" : "Test";
    public static string ChooseSound => Language == AppLanguage.Chinese ? "选择铃声…" : "Choose ringtone…";
    public static string ResetSound => Language == AppLanguage.Chinese ? "重置" : "Reset";
    public static string SoundMissing => Language == AppLanguage.Chinese ? "文件不存在" : "file missing";
    public static string ChooseReminderSound => Language == AppLanguage.Chinese ? "选择提醒铃声" : "Choose reminder ringtone";
    public static string SoundFileFilter => Language == AppLanguage.Chinese
        ? "音频文件 (*.wav)|*.wav|所有文件 (*.*)|*.*"
        : "Audio files (*.wav)|*.wav|All files (*.*)|*.*";
    public static string SoundFileHint => Language == AppLanguage.Chinese
        ? "支持 .wav 音频文件；未设置时使用内置铃声（不依赖 Windows 系统音效方案）。"
        : "WAV audio files. Uses the built-in chime when unset (independent of the Windows sound scheme).";
    public static string InvalidTime => Language == AppLanguage.Chinese
        ? "请输入有效时间（小时 0-23，分钟 0-59）"
        : "Enter a valid time (hour 0-23, minute 0-59)";
    public static string About => Language == AppLanguage.Chinese ? "关于" : "About";
    public static string VersionLabel => Language == AppLanguage.Chinese ? "版本" : "Version";
    public static string AppDescription => Language == AppLanguage.Chinese
        ? "一款 Fluent Design 风格的待办事项桌面应用"
        : "A Fluent Design-style todo desktop app";
    public static string Homepage => Language == AppLanguage.Chinese ? "项目主页" : "Homepage";
    public static string ThirdPartyLicenses => Language == AppLanguage.Chinese ? "第三方组件许可" : "Third-party licenses";

    // Sync settings
    public static string SyncSectionTitle => Language == AppLanguage.Chinese ? "同步" : "Sync";
    public static string SyncEnabledLabel => Language == AppLanguage.Chinese ? "启用多设备同步" : "Enable multi-device sync";
    public static string SyncServerUrlLabel => Language == AppLanguage.Chinese ? "服务器地址" : "Server URL";
    public static string SyncKeyLabel => Language == AppLanguage.Chinese ? "同步密钥" : "Sync key";
    public static string SyncDeviceId => Language == AppLanguage.Chinese ? "设备 ID" : "Device ID";
    public static string SyncStatusLabel => Language == AppLanguage.Chinese ? "状态" : "Status";
    public static string SyncNow => Language == AppLanguage.Chinese ? "立即同步" : "Sync now";
    public static string SyncStatusDisabled => Language == AppLanguage.Chinese ? "同步已禁用" : "Sync disabled";
    public static string SyncStatusNotConfigured => Language == AppLanguage.Chinese
        ? "未配置服务器地址或同步密钥" : "Server URL or sync key not set";
    public static string SyncStatusSyncing => Language == AppLanguage.Chinese ? "同步中…" : "Syncing…";
    public static string SyncStatusOnline => Language == AppLanguage.Chinese ? "已同步" : "Synced";
    public static string SyncStatusOffline => Language == AppLanguage.Chinese ? "同步失败" : "Sync failed";
    public static string SyncStatusAuthFailed => Language == AppLanguage.Chinese ? "同步密钥被拒绝（401）" : "Sync key rejected (401)";
    public static string SyncStatusVersionMismatch => Language == AppLanguage.Chinese
        ? "服务器版本不符，请更新同步服务器" : "Server version mismatch — update the server";
    public static string SyncNever => Language == AppLanguage.Chinese ? "从未同步" : "Never synced";
    public static string SyncLastSynced => Language == AppLanguage.Chinese ? "上次同步" : "Last synced";
    public static string SyncMyDayLocalHint => Language == AppLanguage.Chinese
        ? "「我的一天」仅保存在本设备，不同步。重要标记与其他内容会跨设备同步。"
        : "My Day stays on this device. Important markers and everything else sync across devices.";

    // Behavior + tray
    public static string Behavior => Language == AppLanguage.Chinese ? "行为" : "Behavior";
    public static string MinimizeToTrayOnClose => Language == AppLanguage.Chinese
        ? "关闭主窗口时最小化到托盘" : "Minimize to tray on close";
    public static string StickyShowTags => Language == AppLanguage.Chinese
        ? "在便笺中显示标签" : "Show tags in sticky note";
    public static string TaskRowDisplay => Language == AppLanguage.Chinese
        ? "任务列表显示" : "Task row display";
    public static string ShowTaskTags => Language == AppLanguage.Chinese
        ? "显示标签" : "Show tags";
    public static string ShowTaskSteps => Language == AppLanguage.Chinese
        ? "显示步骤进度" : "Show step progress";
    public static string ShowTaskDue => Language == AppLanguage.Chinese
        ? "显示截止日期" : "Show due date";
    public static string ShowTaskReminder => Language == AppLanguage.Chinese
        ? "显示提醒" : "Show reminders";
    public static string ShowTaskNote => Language == AppLanguage.Chinese
        ? "显示备注图标" : "Show note icon";
    public static string ShowTaskAttachments => Language == AppLanguage.Chinese
        ? "显示附件图标" : "Show attachment icon";
    public static string StickyNote => Language == AppLanguage.Chinese ? "迷你便笺" : "Sticky note";
    public static string OpenMainWindow => Language == AppLanguage.Chinese ? "打开主界面" : "Open main window";
    public static string ExitApp => Language == AppLanguage.Chinese ? "退出" : "Exit";
    public static string StickyCloseNote => Language == AppLanguage.Chinese ? "关闭便笺" : "Close note";
    public static string BackToMain => Language == AppLanguage.Chinese ? "返回主界面" : "Back to main";
}
