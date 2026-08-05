using System.ComponentModel;
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
    public static string Delete => Language == AppLanguage.Chinese ? "删除" : "Delete";
    public static string Complete => Language == AppLanguage.Chinese ? "完成" : "Complete";
    public static string Cancel => Language == AppLanguage.Chinese ? "取消" : "Cancel";
    public static string AddToMyDay => Language == AppLanguage.Chinese ? "添加到我的一天" : "Add to My Day";
    public static string RemoveFromMyDay => Language == AppLanguage.Chinese ? "从我的一天移除" : "Remove from My Day";
    public static string MarkImportant => Language == AppLanguage.Chinese ? "标记为重要" : "Mark as important";
    public static string RemoveImportance => Language == AppLanguage.Chinese ? "取消重要标记" : "Remove importance";
    public static string MoveToList => Language == AppLanguage.Chinese ? "移动到列表" : "Move to list";
    public static string MoveToGroup => Language == AppLanguage.Chinese ? "移动到分组" : "Move to group";
    public static string Ungrouped => Language == AppLanguage.Chinese ? "未分组" : "Ungrouped";
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
    public static string Overdue => Language == AppLanguage.Chinese ? "逾期" : "Overdue";
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
    public static string RelativeDate(DateTime dt) =>
        Language == AppLanguage.Chinese ? $"{dt.Year}年{dt.Month}月{dt.Day}日" : dt.ToString("MMM d, yyyy");

    // Short date (DueDateToStringConverter)
    public static string ShortDate(DateTime dt) =>
        Language == AppLanguage.Chinese ? $"{dt.Month}月{dt.Day}日" : dt.ToString("MMM d");
    public static string OverdueDate(DateTime dt) =>
        Language == AppLanguage.Chinese ? $"逾期 {dt.Month}月{dt.Day}日" : $"Overdue {dt:MMM d}";
    public static string ReminderTime(DateTime dt) =>
        Language == AppLanguage.Chinese ? $"{dt.Month}月{dt.Day}日 {dt:HH:mm}" : dt.ToString("MMM d, HH:mm");
    public static string Reminder => Language == AppLanguage.Chinese ? "提醒" : "Reminder";
    public static string AddDueDate => Language == AppLanguage.Chinese ? "添加截止日期" : "Add due date";
    public static string AddReminder => Language == AppLanguage.Chinese ? "添加提醒" : "Add reminder";
    public static string UpdateAvailable => Language == AppLanguage.Chinese ? "发现新版本" : "Update available";
    public static string DownloadUpdate => Language == AppLanguage.Chinese ? "立即更新" : "Update now";
    public static string RemindLater => Language == AppLanguage.Chinese ? "以后再说" : "Remind me later";
    public static string SkipVersion => Language == AppLanguage.Chinese ? "跳过此版本" : "Skip this version";
    public static string UpdateDownloaded => Language == AppLanguage.Chinese ? "已下载到" : "Downloaded to";
    public static string UpdateUpToDate => Language == AppLanguage.Chinese ? "已是最新版本" : "You're up to date";
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
    public static string PlayReminderSound => Language == AppLanguage.Chinese ? "播放提示音" : "Play reminder sound";
}
