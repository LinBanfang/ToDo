using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToDo.Converters;
using ToDo.Models;
using ToDo.Plugins;
using ToDo.Services;

namespace ToDo.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly IClock _clock;
    private readonly TodoEvents _events;

    // ─── Collections ──────────────────────────────────────
    public ObservableCollection<TaskList> Lists { get; } = new();
    public ObservableCollection<TaskList> SystemLists { get; } = new();
    public ObservableCollection<TaskList> CustomLists { get; } = new();
    public ObservableCollection<ListGroup> ListGroups { get; } = new();
    public ObservableCollection<TaskList> UngroupedCustomLists { get; } = new();
    public ObservableCollection<ListGroupDisplay> GroupedCustomLists { get; } = new();
    public ObservableCollection<TaskGroup> Groups { get; } = new();
    public ObservableCollection<TaskItem> Tasks { get; } = new();
    public ObservableCollection<Tag> Tags { get; } = new();

    // ─── Plugin sidebar entries (filled by PluginManager via IUiHost) ──
    public ObservableCollection<PluginEntryVm> PluginEntries { get; } = new();

    // ─── Active list's tasks (filtered + sorted) ──────────
    public ObservableCollection<TaskItem> ActiveTasks { get; } = new();
    public ObservableCollection<TaskItem> CompletedTasks { get; } = new();

    // ─── Selection ────────────────────────────────────────
    [ObservableProperty]
    private TaskList? _activeList;

    [ObservableProperty]
    private string? _activeListId;

    [ObservableProperty]
    private string? _lastCreatedListId;

    [ObservableProperty]
    private TaskItem? _selectedTask;

    // ─── Search ───────────────────────────────────────────
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    // ─── Drag & Drop ──────────────────────────────────────
    /// <summary>True while a task-row drag is in flight. The empty-ungrouped drop slot
    /// is shown only during a drag — a permanent placeholder would be visual noise.
    /// The slot itself is driven from code-behind (MainWindow.SetDropSlotOpen).</summary>
    [ObservableProperty]
    private bool _isTaskDragging;

    // ─── Theme ────────────────────────────────────────────
    [ObservableProperty]
    private string _theme = "Light"; // Light, Dark

    // ─── Sidebar ──────────────────────────────────────────
    [ObservableProperty]
    private GridLength _sidebarWidth = new(280);

    partial void OnSidebarWidthChanged(GridLength value)
    {
        SettingsService.Current.SidebarWidth = value.Value;
        SettingsService.Save();
    }

    // ─── Sync indicator (sidebar footer dot) ────────────────
    // Mirrors FluentColors' TextDisabledBrush — only a fallback before App.Sync exists.
    private static readonly Brush _idleSyncBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0x9F, 0x9D));

    /// <summary>Whether the sticky note shows tag pills on task rows (live from settings).</summary>
    public bool StickyShowTags => SettingsService.Current.StickyShowTags;

    // ─── Task-row meta toggles (live from settings) ─────
    public bool ShowTaskTags => SettingsService.Current.ShowTaskTags;
    public bool ShowTaskSteps => SettingsService.Current.ShowTaskSteps;
    public bool ShowTaskDue => SettingsService.Current.ShowTaskDue;
    public bool ShowTaskReminder => SettingsService.Current.ShowTaskReminder;
    public bool ShowTaskNote => SettingsService.Current.ShowTaskNote;
    public bool ShowTaskAttachments => SettingsService.Current.ShowTaskAttachments;

    public Brush SyncStatusBrush => App.Sync?.StatusBrush ?? _idleSyncBrush;
    public string SyncStatusText => App.Sync?.StatusText ?? Loc.SyncStatusDisabled;
    /// <summary>True while a round-trip is in flight — drives the sync icon's spin.</summary>
    public bool SyncIsSyncing => App.Sync?.Status == SyncStatus.Syncing;

    // ─── Dialog state ─────────────────────────────────────
    [ObservableProperty]
    private bool _isTagDialogOpen;

    [ObservableProperty]
    private bool _isDateTimeDialogOpen;

    [ObservableProperty]
    private TaskItem? _dateTimeTargetTask;

    // ─── Settings page ────────────────────────────────────
    [ObservableProperty]
    private bool _isSettingsMode;

    public SettingsViewModel Settings { get; }

    partial void OnIsSettingsModeChanged(bool value)
    {
        // Hide the detail pane while the settings page covers the main area
        if (value) SelectedTask = null;
    }

    // ─── Grouped tasks for current list ──────────────────
    public ObservableCollection<GroupedTasks> GroupedTaskList { get; } = new();
    public bool IsCustomList => ActiveList?.Type == ListType.Custom && !IsSearching;
    public bool IsSystemList => !IsCustomList;
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchQuery);
    public string CompletedHeader => $"{Loc.CompletedSection} ({CompletedTasks.Count})";
    public string HeaderTitle => !string.IsNullOrWhiteSpace(SearchQuery)
        ? Loc.SearchResults
        : ActiveList?.DisplayName ?? "";
        public string DbPath => _db.StoragePath;

    private bool IsToday(long ts)
    {
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
        return dt.Date == _clock.Today;
    }

    public MainViewModel(DatabaseService db, IClock? clock = null, TodoEvents? events = null)
    {
        _db = db;
        _clock = clock ?? SystemClock.Instance;
        _events = events ?? new TodoEvents();
        Theme = SettingsService.Current.Theme;
        SidebarWidth = new GridLength(Math.Max(SettingsService.Current.SidebarWidth, 180));
        Settings = new SettingsViewModel();
        // Converge duplicate series instances left by offline races BEFORE loading them
        // into memory, so the UI never shows a series with two open copies (ADR-015).
        RecurrenceService.DedupeSeries(_db);
        LoadAll();
        DailyMyDayReset();
        if (App.Sync != null) App.Sync.StatusChanged += OnSyncStatusChanged;
        SettingsService.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>Re-read the row-display toggles after any settings save so the live
    /// task list immediately reflects changes made on the settings page.</summary>
    private void OnSettingsChanged()
    {
        OnPropertyChanged(nameof(ShowTaskTags));
        OnPropertyChanged(nameof(ShowTaskSteps));
        OnPropertyChanged(nameof(ShowTaskDue));
        OnPropertyChanged(nameof(ShowTaskReminder));
        OnPropertyChanged(nameof(ShowTaskNote));
        OnPropertyChanged(nameof(ShowTaskAttachments));
        OnPropertyChanged(nameof(HeaderTitleLight));   // app theme changed → image-mask recommendation may flip
    }

    private void OnSyncStatusChanged()
    {
        OnPropertyChanged(nameof(SyncStatusBrush));
        OnPropertyChanged(nameof(SyncStatusText));
        OnPropertyChanged(nameof(SyncIsSyncing));
    }

    /// <summary>Clicking the sidebar sync icon kicks an immediate round-trip
    /// (same action as the settings page's "Sync now").</summary>
    [RelayCommand]
    private void SyncNow() => App.Sync?.Trigger();

    /// <summary>Remove undone yesterday tasks from My Day (internal for tests — a
    /// fake clock makes the "yesterday / today" boundary deterministic).</summary>
    internal void DailyMyDayReset()
    {
        var today = _clock.Today;
        var yesterdayTasks = Tasks.Where(t =>
            t.IsMyDay && t.CloseRecord == null && t.DueDate != null
            && !IsToday(t.DueDate.Value)
            && DateTimeOffset.FromUnixTimeMilliseconds(t.DueDate.Value).LocalDateTime.Date < today
        ).ToList();

        foreach (var t in yesterdayTasks)
        {
            t.IsMyDay = false;
            t.MyDayOrder = -1;
            _db.UpdateMyDayLocal(t);
        }

        // Auto-add today's tasks to My Day
        var todayTasks = Tasks.Where(t =>
            !t.IsMyDay && t.CloseRecord == null && t.DueDate != null
            && IsToday(t.DueDate.Value)
        ).ToList();

        var maxOrder = Tasks.Where(t => t.IsMyDay).Select(t => t.MyDayOrder).DefaultIfEmpty(-1).Max();
        foreach (var t in todayTasks)
        {
            t.IsMyDay = true;
            t.MyDayOrder = ++maxOrder;
            _db.UpdateMyDayLocal(t);
        }
    }

    // ─── Data Loading ─────────────────────────────────────
    public void LoadAll()
    {
        LoadListGroups();
        LoadLists();
        LoadGroups();
        LoadTasks();
        LoadTags();

        if (ActiveList == null)
        {
            ActiveList = Lists.FirstOrDefault(l => l.Id == "list-tasks");
            ActiveListId = ActiveList?.Id;
        }
    }

    /// <summary>Public refresh for external callers (drag-drop, etc.)</summary>
    public void NotifyHeaderTitleChanged() => OnPropertyChanged(nameof(HeaderTitle));

    private void RefreshSelectedTask()
    {
        if (SelectedTask != null)
            SelectedTask = Tasks.FirstOrDefault(t => t.Id == SelectedTask.Id);
    }

    public void Refresh()
    {
        LoadGroups();
        LoadLists(); // LoadLists now re-points ActiveList internally
        LoadTasks();
        RefreshActiveTasks();
    }

    /// <summary>Post-sync refresh hook (ADR-010/020)：全量重载 + 重建派生视图 + 广播同步事件。
    /// 由 <see cref="App.Sync"/> 在每轮 round-trip 应用变更后调用。</summary>
    public void OnSyncApplied()
    {
        LoadAll();
        RefreshActiveTasks();
        _events.RaiseDataSyncApplied();
    }

    private void LoadListGroups()
    {
        var all = _db.ListGroups.Query().OrderBy(x => x.Order).ToArray();
        ListGroups.Clear();
        foreach (var g in all) ListGroups.Add(g);
    }

    private void RebuildSidebarGroups()
    {
        UngroupedCustomLists.Clear();
        GroupedCustomLists.Clear();

        var ungrouped = CustomLists.Where(l => l.GroupId == null).OrderBy(l => l.Order).ToList();
        foreach (var l in ungrouped) UngroupedCustomLists.Add(l);

        foreach (var g in ListGroups.OrderBy(g => g.Order))
        {
            var lists = CustomLists.Where(l => l.GroupId == g.Id).OrderBy(l => l.Order).ToList();
            GroupedCustomLists.Add(new ListGroupDisplay
            {
                Group = g,
                Lists = new ObservableCollection<TaskList>(lists)
            });
        }
    }

    private void LoadLists()
    {
        var all = _db.Lists.Query().OrderBy(x => x.Order).ToArray();
        var allTasks = _db.Tasks.FindAll().ToList();
        foreach (var l in all)
            l.TaskCount = CountForList(l, allTasks);

        // Sync in-place: update existing, add new, remove deleted
        var allIds = new HashSet<string>(all.Select(l => l.Id));
        // Remove deleted
        for (int i = Lists.Count - 1; i >= 0; i--)
            if (!allIds.Contains(Lists[i].Id))
                Lists.RemoveAt(i);
        for (int i = SystemLists.Count - 1; i >= 0; i--)
            if (!allIds.Contains(SystemLists[i].Id))
                SystemLists.RemoveAt(i);
        for (int i = CustomLists.Count - 1; i >= 0; i--)
            if (!allIds.Contains(CustomLists[i].Id))
                CustomLists.RemoveAt(i);

        foreach (var l in all)
        {
            l.IsRenaming = false;
            var existing = Lists.FirstOrDefault(x => x.Id == l.Id);
            if (existing != null)
            {
                existing.Name = l.Name;
                existing.Icon = l.Icon;
                existing.Order = l.Order;
                existing.TaskCount = l.TaskCount;
                existing.IsRenaming = false;
            }
            else
            {
                Lists.Add(l);
                if (l.IsSystem) SystemLists.Add(l);
                else CustomLists.Add(l);
            }
        }

        // Re-point ActiveList if needed
        if (ActiveList != null)
        {
            var fresh = Lists.FirstOrDefault(x => x.Id == ActiveList.Id);
            if (fresh != null && !ReferenceEquals(ActiveList, fresh))
            {
                ActiveList = fresh;
                ActiveListId = fresh.Id;
            }
        }

        RebuildSidebarGroups();
    }

    private void LoadGroups()
    {
        Groups.Clear();
        foreach (var g in _db.Groups.Query().OrderBy(x => x.Order).ToArray())
            Groups.Add(g);
    }

    private void LoadTasks()
    {
        Tasks.Clear();
        foreach (var t in _db.Tasks.FindAll().OrderBy(x => x.Order))
            Tasks.Add(t);
        _db.RefreshAttachmentCounts(Tasks);   // row paperclip counts (local-only, ADR-013)

        // Re-point the selection so the detail pane always edits the live instance
        RefreshSelectedTask();
    }

    private void LoadTags()
    {
        Tags.Clear();
        foreach (var t in _db.Tags.FindAll())
            Tags.Add(t);
    }

    /// <summary>Unclosed task count for a list (sidebar badge)</summary>
    private int CountForList(TaskList list, IEnumerable<TaskItem> tasks)
    {
        return list.Type switch
        {
            ListType.MyDay => tasks.Count(t =>
                t.CloseRecord == null && (t.IsMyDay || (t.DueDate != null && IsToday(t.DueDate.Value)))),
            ListType.Important => tasks.Count(t => t.IsImportant && t.CloseRecord == null),
            ListType.Planned => tasks.Count(t =>
                (t.DueDate != null || t.Reminder != null) && t.CloseRecord == null),
            ListType.Tasks => tasks.Count(t =>
                t.CloseRecord == null && t.ListId == "list-tasks"),
            _ => tasks.Count(t => t.ListId == list.Id && t.CloseRecord == null),
        };
    }

    /// <summary>Next order slot after the highest existing one, so gaps don't cause duplicates</summary>
    private static int NextOrder(IEnumerable<int> existing) =>
        existing.DefaultIfEmpty(-1).Max() + 1;

    // ─── Refresh derived data ────────────────────────────
    public void RefreshActiveTasks()
    {
        ActiveTasks.Clear();
        CompletedTasks.Clear();
        GroupedTaskList.Clear();

        if (ActiveList == null) return;

        var isSearching = !string.IsNullOrWhiteSpace(SearchQuery);

        // Sidebar counts don't change while searching; skip the per-keystroke scan
        if (!isSearching)
        {
            foreach (var l in Lists)
                l.TaskCount = CountForList(l, Tasks);
        }

        // Searching → across ALL lists; otherwise → filter by current list
        var allListTasks = isSearching
            ? Tasks.ToList()
            : ActiveList.Type == ListType.Custom
                ? Tasks.Where(t => t.ListId == ActiveList.Id).ToList()
                : Tasks.ToList();

        // Active (unclosed) tasks
        var active = allListTasks.Where(t => t.CloseRecord == null);
        var completed = allListTasks.Where(t => t.CloseRecord != null);

        // Apply search filter
        if (isSearching)
        {
            var q = SearchQuery.ToLower();
            active = active.Where(t =>
                t.Title.ToLower().Contains(q) ||
                (t.Note?.ToLower().Contains(q) ?? false));
            completed = completed.Where(t =>
                t.Title.ToLower().Contains(q) ||
                (t.Note?.ToLower().Contains(q) ?? false));
        }

        // Sort based on list type
        var sortedActive = isSearching
            ? active.OrderByDescending(t => t.ModifiedAt)
            : ActiveList.Type switch
            {
                ListType.MyDay => active
                    .Where(t => t.IsMyDay || (t.DueDate != null && IsToday(t.DueDate.Value)))
                    .OrderBy(t => t.MyDayOrder),
                ListType.Important => active.Where(t => t.IsImportant).OrderByDescending(t => t.ModifiedAt),
                ListType.Planned => active
                    .Where(t => t.DueDate != null || t.Reminder != null)
                    .OrderBy(t => t.DueDate ?? long.MaxValue),
                ListType.Tasks => active
                    .Where(t => t.ListId == "list-tasks")
                    .OrderByDescending(t => t.ModifiedAt),
                _ => active.OrderBy(t => t.Order)
            };

        foreach (var t in sortedActive)
            ActiveTasks.Add(t);

        // Filter completed by the same view logic (skip during search)
        var filteredCompleted = isSearching
            ? completed
            : ActiveList.Type switch
            {
                ListType.MyDay => completed.Where(t =>
                    t.IsMyDay || (t.DueDate != null && IsToday(t.DueDate.Value))),
                ListType.Important => completed.Where(t => t.IsImportant),
                ListType.Planned => completed.Where(t => t.DueDate != null || t.Reminder != null),
                ListType.Tasks => completed.Where(t => t.ListId == "list-tasks"),
                _ => completed.Where(t => t.ListId == ActiveList.Id)
            };

        var sortedCompleted = filteredCompleted.OrderByDescending(t =>
            t.CloseRecord?.ClosedAt ?? 0);
        foreach (var t in sortedCompleted)
            CompletedTasks.Add(t);

        OnPropertyChanged(nameof(CompletedHeader));

        // Build grouped view for custom lists (skip when searching)
        if (ActiveList.Type == ListType.Custom && !isSearching)
        {
            // Ungrouped section (always shown as drop target)
            var ungrouped = ActiveTasks.Where(t => t.GroupId == null).ToList();
            GroupedTaskList.Add(new GroupedTasks
            {
                Group = null!,
                Tasks = new ObservableCollection<TaskItem>(ungrouped)
            });

            // Then actual groups
            var groups = Groups.Where(g => g.ListId == ActiveList.Id).OrderBy(g => g.Order);
            foreach (var g in groups)
            {
                var groupTasks = ActiveTasks.Where(t => t.GroupId == g.Id).ToList();
                GroupedTaskList.Add(new GroupedTasks { Group = g, Tasks = new ObservableCollection<TaskItem>(groupTasks) });
            }
        }
    }

    partial void OnActiveListChanged(TaskList? value)
    {
        ActiveListId = value?.Id;
        SelectedTask = null;
        SearchQuery = string.Empty;
        OnPropertyChanged(nameof(IsCustomList));
        OnPropertyChanged(nameof(IsSystemList));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(ListBackgroundBrush));
        OnPropertyChanged(nameof(ListBackgroundMaskVisible));
        OnPropertyChanged(nameof(HeaderTitleLight));
        // The shared card brushes follow the active list's card opacity (ADR-014).
        ThemeService.SetCardOpacity(ActiveCardOpacity);
        // No need to reload Tasks: the in-place model keeps it current on every
        // mutation, so a list switch only needs the views rebuilt.
        RefreshActiveTasks();
        // Selecting a sidebar list exits the settings page to show that list
        IsSettingsMode = false;
    }

    partial void OnActiveListIdChanged(string? value)
    {
        if (value != null && ActiveList?.Id != value)
        {
            ActiveList = Lists.FirstOrDefault(l => l.Id == value);
        }
    }

    partial void OnSelectedTaskChanged(TaskItem? value)
    {
        // Load local-only attachments (ADR-013) into the [BsonIgnore] list the detail
        // pane binds to. Runs before the SelectedTask PropertyChanged fires, so WPF
        // re-evaluating the binding sees the populated collection.
        if (value != null)
        {
            value.Attachments.Clear();
            foreach (var a in _db.GetAttachments(value.Id))
                value.Attachments.Add(a);
            _db.RefreshAttachmentCounts(new[] { value });
        }
        // Detail pane pickers are refreshed by the view
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            SelectedTask = null;
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(IsCustomList));
        OnPropertyChanged(nameof(IsSystemList));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(ListBackgroundBrush));
        OnPropertyChanged(nameof(ListBackgroundMaskVisible));
        OnPropertyChanged(nameof(HeaderTitleLight));
        // Tasks is always current in this single-process app (every mutation
        // reloads it), so filter it in memory instead of re-reading the DB
        // and rebuilding the collection on every keystroke.
        RefreshActiveTasks();
    }

}

/// <summary>
/// Helper for grouping tasks by group in the UI
/// </summary>
/// <summary>Display wrapper for list groups in sidebar</summary>
public partial class ListGroupDisplay : ObservableObject
{
    [ObservableProperty] private ListGroup _group = null!;
    [ObservableProperty] private ObservableCollection<TaskList> _lists = new();
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _editName = string.Empty;
    public bool HasGroup => Group != null;
    public bool TaskListVisible => !Group.Collapsed;
}

public partial class GroupedTasks : ObservableObject
{
    [ObservableProperty]
    private TaskGroup? _group;

    [ObservableProperty]
    private ObservableCollection<TaskItem> _tasks = new();

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = string.Empty;

    public bool HasGroup => Group != null;
    public bool TaskListVisible => Group == null || !Group.Collapsed;

    /// <summary>True for the ungrouped section when it holds no tasks: the wrapper
    /// Border is zero-height then, so the UI shows a hint strip as the drop target
    /// for dragging a grouped task back to ungrouped.</summary>
    public bool ShowEmptyUngroupedHint => Group == null && Tasks.Count == 0;
}
