using System.Globalization;
using System.Resources;
using ToDo.Models;

namespace ToDo.Services;

public enum AppLanguage { English, Chinese }

/// <summary>
/// Typed facade over the RESX resources (Strings.resx = Chinese neutral +
/// Strings.en.resx satellite), kept in place of a generated designer so the
/// public member surface stays byte-identical to the old ternary implementation:
/// ~127 XAML <c>{x:Static services:Loc.X}</c> bindings and ~216 C# call sites
/// depend on these exact member names. Adding a language = add a Strings.xx.resx,
/// an <see cref="AppLanguage"/> value and a culture mapping in <see cref="SetLanguage"/>
/// (see docs/adr/0016-localization-resx.md).
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Res = new("ToDo.Resources.Strings", typeof(Loc).Assembly);
    private static CultureInfo _culture = CultureInfo.GetCultureInfo("zh-CN"); // default = Chinese

    public static AppLanguage Language { get; private set; } = AppLanguage.Chinese;
    public static event Action? LanguageChanged;

    /// <summary>Look up a key under the active culture; a missing key visibly
    /// degrades to ⟦Key⟧ (never the bare key — several English values legitimately
    /// equal their key, e.g. "OK"), and the tests assert no ⟦ sentinel leaks.</summary>
    private static string S(string key)
    {
        try { return Res.GetString(key, _culture) ?? "⟦" + key; }
        catch (MissingManifestResourceException) { return "⟦" + key; }
    }

    public static void SetLanguage(AppLanguage lang)
    {
        if (Language != lang)
        {
            Language = lang;
            _culture = lang == AppLanguage.English
                ? CultureInfo.GetCultureInfo("en-US")
                : CultureInfo.GetCultureInfo("zh-CN");
            LanguageChanged?.Invoke();
        }
    }

    public static void Toggle()
    {
        SetLanguage(Language == AppLanguage.Chinese ? AppLanguage.English : AppLanguage.Chinese);
    }

    // ─── String properties ────────────────────────────────
    public static string AppTitle => S("AppTitle");
    public static string Search => S("Search");
    public static string SearchResults => S("SearchResults");
    public static string System => S("System");
    public static string Lists => S("Lists");
    public static string Tags => S("Tags");
    public static string AddTask => S("AddTask");
    public static string NewList => S("NewList");
    public static string NewListText => S("NewListText");
    public static string NewGroup => S("NewGroup");
    public static string NewListGroup => S("NewListGroup");
    public static string DeleteListGroup => S("DeleteListGroup");
    public static string TaskDetails => S("TaskDetails");
    public static string Steps => S("Steps");
    public static string AddStep => S("AddStep");
    public static string Closed => S("Closed");
    public static string Completed => S("Completed");
    public static string Cancelled => S("Cancelled");
    public static string EditCloseTime => S("EditCloseTime");
    public static string Notes => S("Notes");
    public static string Attachments => S("Attachments");
    public static string AddAttachment => S("AddAttachment");
    public static string NoAttachments => S("NoAttachments");
    public static string Delete => S("Delete");
    public static string Complete => S("Complete");
    public static string Cancel => S("Cancel");
    public static string OK => S("OK");
    public static string AddToMyDay => S("AddToMyDay");
    public static string RemoveFromMyDay => S("RemoveFromMyDay");
    public static string MarkImportant => S("MarkImportant");
    public static string RemoveImportance => S("RemoveImportance");
    public static string MoveToList => S("MoveToList");
    public static string MoveToGroup => S("MoveToGroup");
    public static string Ungrouped => S("Ungrouped");
    public static string DropToUngroupHint => S("DropToUngroupHint");
    public static string RemoveFromGroup => S("RemoveFromGroup");
    public static string ReopenTask => S("ReopenTask");
    public static string DeleteTask => S("DeleteTask");
    public static string DeleteGroup => S("DeleteGroup");
    public static string Rename => S("Rename");
    public static string Group => S("Group");
    public static string RenameList => S("RenameList");
    public static string DeleteList => S("DeleteList");
    public static string ManageTags => S("ManageTags");
    public static string AddTag => S("AddTag");
    public static string NoTags => S("NoTags");
    public static string ConfirmDelete => S("ConfirmDelete");
    public static string CompletedSection => S("CompletedSection");
    public static string EditTime => S("EditTime");
    public static string Date => S("Date");
    public static string Time => S("Time");
    public static string Save => S("Save");
    public static string TagPlaceholder => S("TagPlaceholder");
    public static string Add => S("Add");
    public static string Done => S("Done");
    public static string Today => S("Today");
    public static string Tomorrow => S("Tomorrow");
    public static string ThisWeek => S("ThisWeek");
    public static string Later => S("Later");
    public static string PickDate => S("PickDate");
    public static string NoDueDate => S("NoDueDate");

    // System list display names
    public static string MyDay => S("MyDay");
    public static string Important => S("Important");
    public static string Planned => S("Planned");
    public static string Tasks => S("Tasks");
    public static string DbPathTitle => S("DbPathTitle");
    public static string DbPathPrompt => S("DbPathPrompt");
    public static string DbPathChanged => S("DbPathChanged");
    public static string SelectDbFile => S("SelectDbFile");
    public static string DbFileFilter => S("DbFileFilter");
    public static string InvalidPathMsg => S("InvalidPathMsg");
    public static string Error => S("Error");
    public static string Yesterday => S("Yesterday");
    public static string NewListName => S("NewListName");
    public static string MarkIncomplete => S("MarkIncomplete");
    public static string PromoteToTask => S("PromoteToTask");

    // Undo bar + reminder toast buttons
    public static string Undo => S("Undo");
    public static string SnoozeReminder => S("SnoozeReminder");
    public static string OpenTask => S("OpenTask");

    // Reminder toast auto-close duration (settings)
    public static string ReminderToastDuration => S("ReminderToastDuration");
    public static string ToastSeconds5 => S("ToastSeconds5");
    public static string ToastSeconds10 => S("ToastSeconds10");
    public static string ToastSeconds30 => S("ToastSeconds30");
    public static string ToastNeverAutoClose => S("ToastNeverAutoClose");
    public static string ReminderToastPauseHint => S("ReminderToastPauseHint");

    // Keyboard shortcuts (settings hint)
    public static string KeyboardShortcuts => S("KeyboardShortcuts");
    public static string KeyboardShortcutsHint => S("KeyboardShortcutsHint");

    // Relative time (TimestampToRelativeStringConverter)
    public static string JustNow => S("JustNow");
    // The date methods format with InvariantCulture so an English user on a zh-CN
    // system still sees "Mar 5, 2024" — the format templates themselves come from
    // RESX (RelativeDateFormat / ShortDateFormat / ReminderTimeFormat).
    public static string RelativeDate(DateTime dt) =>
        string.Format(CultureInfo.InvariantCulture, S("RelativeDateFormat"), dt);

    // Short date (DueDateToStringConverter)
    public static string ShortDate(DateTime dt) =>
        string.Format(CultureInfo.InvariantCulture, S("ShortDateFormat"), dt);
    public static string ReminderTime(DateTime dt) =>
        string.Format(CultureInfo.InvariantCulture, S("ReminderTimeFormat"), dt);
    // A reminder falling today shows just the time on the task row.
    public static string ReminderTimeOnly(DateTime dt) => dt.ToString("HH:mm", CultureInfo.InvariantCulture);
    // A reminder on another day shows just the date on the task row (no clock time).
    public static string ReminderDateOnly(DateTime dt) =>
        string.Format(CultureInfo.InvariantCulture, S("ShortDateFormat"), dt);
    public static string Reminder => S("Reminder");
    public static string AddDueDate => S("AddDueDate");
    public static string AddReminder => S("AddReminder");
    public static string Recurrence => S("Recurrence");
    public static string AddRecurrence => S("AddRecurrence");
    public static string RepeatNone => S("RepeatNone");
    public static string RepeatDaily => S("RepeatDaily");
    public static string RepeatWeekdays => S("RepeatWeekdays");
    public static string RepeatWeekly => S("RepeatWeekly");
    public static string RepeatMonthly => S("RepeatMonthly");
    public static string RepeatYearly => S("RepeatYearly");
    public static string SkipOccurrence => S("SkipOccurrence");
    public static string EndSeries => S("EndSeries");
    public static string RecurrenceName(RecurrenceFrequency freq) => freq switch
    {
        RecurrenceFrequency.Daily => RepeatDaily,
        RecurrenceFrequency.Weekdays => RepeatWeekdays,
        RecurrenceFrequency.Weekly => RepeatWeekly,
        RecurrenceFrequency.Monthly => RepeatMonthly,
        RecurrenceFrequency.Yearly => RepeatYearly,
        _ => RepeatNone,
    };
    public static string UpdateAvailable => S("UpdateAvailable");
    public static string DownloadUpdate => S("DownloadUpdate");
    public static string RemindLater => S("RemindLater");
    public static string SkipVersion => S("SkipVersion");
    public static string UpdateDownloaded => S("UpdateDownloaded");
    public static string UpdateUpToDate(string latest) =>
        string.Format(S("UpdateUpToDate"), latest);
    public static string UpdateSourceNoInfo => S("UpdateSourceNoInfo");
    public static string UpdateCheckFailed(string detail) =>
        string.Format(S("UpdateCheckFailed"), detail);

    // Settings page
    public static string Settings => S("Settings");
    public static string Back => S("Back");
    public static string General => S("General");
    public static string Appearance => S("Appearance");
    public static string Data => S("Data");
    public static string Updates => S("Updates");
    public static string RemindersSection => S("RemindersSection");
    public static string LanguageName => S("LanguageName");
    public static string Theme => S("Theme");
    public static string LightTheme => S("LightTheme");
    public static string DarkTheme => S("DarkTheme");
    public static string RestartToApply => S("RestartToApply");
    public static string AppliesImmediately => S("AppliesImmediately");
    public static string Change => S("Change");
    public static string ExportBackup => S("ExportBackup");
    public static string RestoreBackup => S("RestoreBackup");
    public static string BackupSaved(string path) =>
        string.Format(S("BackupSaved"), path);
    public static string RestoreStaged => S("RestoreStaged");
    public static string SelectBackupFile => S("SelectBackupFile");
    public static string BackupFileFilter => S("BackupFileFilter");
    public static string CheckForUpdatesOnStartup => S("CheckForUpdatesOnStartup");
    public static string UpdateSources => S("UpdateSources");
    public static string AddSource => S("AddSource");
    public static string RemoveSource => S("RemoveSource");
    public static string CheckUpdatesNow => S("CheckUpdatesNow");
    public static string EnableReminderNotifications => S("EnableReminderNotifications");
    public static string Apply => S("Apply");
    public static string MoreColors => S("MoreColors");
    // List theme dialog (ADR-014)
    public static string ListTheme => S("ListTheme");
    public static string NoBackground => S("NoBackground");
    public static string SolidColor => S("SolidColor");
    public static string ChooseImage => S("ChooseImage");
    public static string RemoveImage => S("RemoveImage");
    public static string ImageTooLarge(int mb) =>
        string.Format(S("ImageTooLarge"), mb);
    public static string ImageFileFilter => S("ImageFileFilter");
    public static string ListThemeImageLocalHint => S("ListThemeImageLocalHint");
    public static string BackgroundStrength => S("BackgroundStrength");
    public static string BackgroundStrengthLocalHint => S("BackgroundStrengthLocalHint");
    public static string CardOpacity => S("CardOpacity");
    public static string CardOpacityLocalHint => S("CardOpacityLocalHint");
    public static string TitleTextColor => S("TitleTextColor");
    public static string TitleTextAuto => S("TitleTextAuto");
    public static string TitleTextDark => S("TitleTextDark");
    public static string TitleTextLight => S("TitleTextLight");
    public static string TitleTextPickHint => S("TitleTextPickHint");
    public static string TitleTextNoRecommend => S("TitleTextNoRecommend");
    public static string TitleTextRecommend(bool light) => Language == AppLanguage.Chinese
        ? $"自动：推荐{(light ? TitleTextLight : TitleTextDark)}文字"
        : $"Auto: recommends {(light ? TitleTextLight : TitleTextDark)} text";
    public static string PlayReminderSound => S("PlayReminderSound");
    public static string ReminderRingtone => S("ReminderRingtone");
    public static string DefaultRingtone => S("DefaultRingtone");
    public static string TestSound => S("TestSound");
    public static string ChooseSound => S("ChooseSound");
    public static string ResetSound => S("ResetSound");
    public static string SoundMissing => S("SoundMissing");
    public static string ChooseReminderSound => S("ChooseReminderSound");
    public static string SoundFileFilter => S("SoundFileFilter");
    public static string SoundFileHint => S("SoundFileHint");
    public static string InvalidTime => S("InvalidTime");
    public static string About => S("About");
    public static string VersionLabel => S("VersionLabel");
    public static string AppDescription => S("AppDescription");
    public static string Homepage => S("Homepage");
    public static string ThirdPartyLicenses => S("ThirdPartyLicenses");

    // Sync settings
    public static string SyncSectionTitle => S("SyncSectionTitle");
    public static string SyncEnabledLabel => S("SyncEnabledLabel");
    public static string SyncServerUrlLabel => S("SyncServerUrlLabel");
    public static string SyncKeyLabel => S("SyncKeyLabel");
    public static string SyncDeviceId => S("SyncDeviceId");
    public static string SyncStatusLabel => S("SyncStatusLabel");
    public static string SyncNow => S("SyncNow");
    public static string SyncStatusDisabled => S("SyncStatusDisabled");
    public static string SyncStatusNotConfigured => S("SyncStatusNotConfigured");
    public static string SyncStatusSyncing => S("SyncStatusSyncing");
    public static string SyncStatusOnline => S("SyncStatusOnline");
    public static string SyncStatusOffline => S("SyncStatusOffline");
    public static string SyncStatusAuthFailed => S("SyncStatusAuthFailed");
    public static string SyncStatusVersionMismatch => S("SyncStatusVersionMismatch");
    public static string SyncNever => S("SyncNever");
    public static string SyncLastSynced => S("SyncLastSynced");
    public static string SyncMyDayLocalHint => S("SyncMyDayLocalHint");
    public static string SyncInsecureUrlHint => S("SyncInsecureUrlHint");

    // Behavior + tray
    public static string Behavior => S("Behavior");
    public static string MinimizeToTrayOnClose => S("MinimizeToTrayOnClose");
    public static string StickyShowTags => S("StickyShowTags");
    public static string TaskRowDisplay => S("TaskRowDisplay");
    public static string ShowTaskTags => S("ShowTaskTags");
    public static string ShowTaskSteps => S("ShowTaskSteps");
    public static string ShowTaskDue => S("ShowTaskDue");
    public static string ShowTaskReminder => S("ShowTaskReminder");
    public static string ShowTaskNote => S("ShowTaskNote");
    public static string ShowTaskAttachments => S("ShowTaskAttachments");
    public static string StickyNote => S("StickyNote");
    public static string OpenMainWindow => S("OpenMainWindow");
    public static string ExitApp => S("ExitApp");
    public static string StickyCloseNote => S("StickyCloseNote");
    public static string BackToMain => S("BackToMain");

    // Format-template methods (values live in RESX with {0} placeholders)
    public static string ConfirmDeleteListGroupMsg(string name) =>
        string.Format(S("ConfirmDeleteListGroupMsg"), name);
    public static string AttachmentOpenFailed(string name) =>
        string.Format(S("AttachmentOpenFailed"), name);
    public static string AttachmentTooLarge(int mb) =>
        string.Format(S("AttachmentTooLarge"), mb);
    public static string ConfirmDeleteMsg(string name) =>
        string.Format(S("ConfirmDeleteMsg"), name);
    public static string TagNameExists(string name) =>
        string.Format(S("TagNameExists"), name);
    public static string ConfirmDeleteGroupMsg(string name) =>
        string.Format(S("ConfirmDeleteGroupMsg"), name);
    public static string UndoCompleteMsg(string title) =>
        string.Format(S("UndoCompleteMsg"), title);
    public static string UndoDeleteMsg(string title) =>
        string.Format(S("UndoDeleteMsg"), title);
    public static string MinutesAgo(int n) =>
        string.Format(S("MinutesAgo"), n);
    public static string HoursAgo(int n) =>
        string.Format(S("HoursAgo"), n);
    public static string DaysAgo(int n) =>
        string.Format(S("DaysAgo"), n);

    // Detail-pane snooze menu (pluralized; Chinese has no plural so both match)
    public static string HoursFromNow(int n) =>
        string.Format(n == 1 ? S("HourLater") : S("HoursLater"), n);
}
