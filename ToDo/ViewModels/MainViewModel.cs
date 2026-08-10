using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToDo.Converters;
using ToDo.Models;
using ToDo.Services;

namespace ToDo.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly IClock _clock;

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

    // ─── Active list background theme (ADR-014) ───────────
    /// <summary>Brush painting the task-area background for the active list's theme.
    /// Null when there's no theme, searching, or no active list — the window's
    /// AppBackgroundBrush then shows through. Rebuilt lazily and re-raised explicitly
    /// (SetListTheme / OnActiveListChanged / OnSearchQueryChanged): LoadLists re-points
    /// ActiveList only when the instance differs, so an in-place theme edit would not
    /// re-trigger a converter bound to ActiveList's fields.</summary>
    public Brush? ListBackgroundBrush
    {
        get
        {
            if (IsSearching || ActiveList == null) return null;
            // Per-list opacity ("背景强弱", local-only): lower fades the background toward
            // the window background. Baked into the brush so solid colors and images share
            // one knob; the readability mask is left untouched.
            var opacity = _db.GetListThemeSettings(ActiveList.Id).Background / 100.0;
            return ActiveList.BackgroundType switch
            {
                ListBackgroundType.Solid => BuildSolidBrush(ActiveList.BackgroundColor, opacity),
                ListBackgroundType.Image => BuildImageBrush(ActiveList.Id, opacity),
                _ => null,
            };
        }
    }

    /// <summary>True when the active list has an image background (dimming mask visible).
    /// Hidden during search so the global background shows across lists.</summary>
    public bool ListBackgroundMaskVisible =>
        !IsSearching && ActiveList?.BackgroundType == ListBackgroundType.Image;

    /// <summary>The active list's card opacity (30..100) — the knob applied to the shared
    /// TaskCardBrush/TaskCardHoverBrush (ADR-014). Default 65 when unset.</summary>
    private int ActiveCardOpacity =>
        ActiveList == null ? 65 : _db.GetListThemeSettings(ActiveList.Id).Card;

    private Brush? BuildSolidBrush(string hex, double opacity)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try
        {
            var brush = new SolidColorBrush(ColorParser.ParseColor(hex)) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }
        catch { return null; }
    }

    private Brush? BuildImageBrush(string listId, double opacity)
    {
        var bytes = _db.GetListBackgroundData(listId);
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;   // read fully before the stream closes
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill, Opacity = opacity };
            brush.Freeze();
            return brush;
        }
        catch { return null; }
    }

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

    public MainViewModel(DatabaseService db, IClock? clock = null)
    {
        _db = db;
        _clock = clock ?? SystemClock.Instance;
        Theme = SettingsService.Current.Theme;
        SidebarWidth = new GridLength(Math.Max(SettingsService.Current.SidebarWidth, 180));
        Settings = new SettingsViewModel();
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
            t.ModifiedAt = _clock.UtcNow.ToUnixTimeMilliseconds();
            _db.Tasks.Update(t);
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
            t.ModifiedAt = _clock.UtcNow.ToUnixTimeMilliseconds();
            _db.Tasks.Update(t);
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
        // Tasks is always current in this single-process app (every mutation
        // reloads it), so filter it in memory instead of re-reading the DB
        // and rebuilding the collection on every keystroke.
        RefreshActiveTasks();
    }

    // ─── List Commands ────────────────────────────────────
    [RelayCommand]
    private void CreateListGroup(string name)
    {
        var g = new ListGroup { Name = name, Order = NextOrder(ListGroups.Select(x => x.Order)) };
        _db.ListGroups.Insert(g);
        LoadListGroups();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void RenameListGroup(ListGroup g)
    {
        _db.ListGroups.Update(g);
        LoadListGroups();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void DeleteListGroup(ListGroup g)
    {
        // Move lists in this group to ungrouped
        var listsInGroup = CustomLists.Where(l => l.GroupId == g.Id).ToList();
        foreach (var l in listsInGroup)
        {
            l.GroupId = null;
            _db.Lists.Update(l);
        }
        _db.ListGroups.Delete(g.Id);
        LoadListGroups();
        LoadLists();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void ToggleListGroupCollapse(ListGroup g)
    {
        g.Collapsed = !g.Collapsed;
        _db.ListGroups.Update(g);
        LoadListGroups();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void MoveListToGroup((TaskList list, ListGroup? group) param)
    {
        param.list.GroupId = param.group?.Id;
        _db.Lists.Update(param.list);
        // Auto-expand target group
        if (param.group != null && param.group.Collapsed)
        {
            param.group.Collapsed = false;
            _db.ListGroups.Update(param.group);
        }
        LoadListGroups();
        LoadLists();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void CreateList(string name)
    {
        var list = new TaskList
        {
            Name = name,
            Icon = "📋",
            Type = ListType.Custom,
            Order = NextOrder(Lists.Where(l => !l.IsSystem).Select(l => l.Order)),
        };
        _db.Lists.Insert(list);
        LastCreatedListId = list.Id;
        LoadLists();
    }

    [RelayCommand]
    private void RenameList(TaskList list)
    {
        _db.Lists.Update(list);
        LoadLists();
    }

    /// <summary>Applies a list's background theme (called by the theme dialog's OK).
    /// The type + color sync via the tracked Lists collection; the image bytes and the
    /// display settings (background strength, card opacity) are local-only in untracked
    /// collections (ADR-014). Raises the background properties explicitly — LoadLists won't
    /// re-point an in-place-edited ActiveList.</summary>
    public void SetListTheme(TaskList list, ListBackgroundType type, string color,
                             byte[]? image, string? fileName, int opacityPercent = 100,
                             int cardOpacity = 65)
    {
        list.BackgroundType = type;
        list.BackgroundColor = color;
        _db.Lists.Update(list);
        if (image != null) _db.SetListBackground(list.Id, image, fileName);
        else _db.DeleteListBackground(list.Id);
        // Display settings only earn a row when either differs from its default, so the
        // collection holds "non-default" settings and a missing row reads back as 100/65.
        _db.SetListThemeSettings(list.Id, opacityPercent, cardOpacity);
        // The shared card brushes reflect the ACTIVE list's opacity (ADR-014); if the dialog
        // edited that list apply the new value now, otherwise it lands when the list becomes
        // active (OnActiveListChanged re-tints).
        if (list.Id == ActiveList?.Id) ThemeService.SetCardOpacity(cardOpacity);
        OnPropertyChanged(nameof(ListBackgroundBrush));
        OnPropertyChanged(nameof(ListBackgroundMaskVisible));
    }

    [RelayCommand]
    private void DeleteList(TaskList list)
    {
        if (list.IsSystem) return;

        // Preserve data: move tasks to the Tasks (inbox) list instead of deleting them
        var movedTasks = _db.Tasks.Find(t => t.ListId == list.Id).ToList();
        foreach (var t in movedTasks)
        {
            t.ListId = "list-tasks";
            t.GroupId = null;
            t.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _db.Tasks.Update(t);
        }

        _db.Groups.DeleteMany(g => g.ListId == list.Id);
        _db.Lists.Delete(list.Id);
        _db.DeleteListBackground(list.Id);   // local background image dies with the list (ADR-014)
        _db.DeleteListBackgroundSetting(list.Id);   // ...and so does its opacity setting
        LoadAll();
        if (ActiveList?.Id == list.Id)
            ActiveList = Lists.FirstOrDefault(l => l.Id == "list-tasks");
    }

    // ─── Group Commands ───────────────────────────────────
    [RelayCommand]
    private void CreateGroup(string name)
    {
        if (ActiveList == null) return;
        var group = new TaskGroup
        {
            Name = name,
            ListId = ActiveList.Id,
            Order = NextOrder(Groups.Where(g => g.ListId == ActiveList.Id).Select(g => g.Order)),
        };
        _db.Groups.Insert(group);
        LoadGroups();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void RenameGroup(TaskGroup group)
    {
        _db.Groups.Update(group);
        LoadGroups();
    }

    [RelayCommand]
    private void DeleteGroup(TaskGroup group)
    {
        // Move tasks to ungrouped
        var tasksInGroup = Tasks.Where(t => t.GroupId == group.Id).ToList();
        foreach (var t in tasksInGroup)
        {
            t.GroupId = null;
            _db.Tasks.Update(t);
        }
        _db.Groups.Delete(group.Id);
        LoadGroups();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void ToggleGroupCollapse(TaskGroup group)
    {
        group.Collapsed = !group.Collapsed;
        _db.Groups.Update(group);
        LoadGroups();
        RefreshActiveTasks();
    }

    // ─── Task Commands ────────────────────────────────────
    [RelayCommand]
    private void CreateTask(string title)
    {
        if (ActiveList == null) return;

        // Searching → inbox; system lists → inbox; custom lists → to that list
        var listId = IsSearching
            ? "list-tasks"
            : ActiveList.Type == ListType.Custom ? ActiveList.Id : "list-tasks";
        var isMyDay = !IsSearching && ActiveList.Type == ListType.MyDay;

        var task = new TaskItem
        {
            Title = title,
            ListId = listId,
            IsMyDay = isMyDay,
            MyDayOrder = isMyDay ? NextOrder(Tasks.Where(t => t.IsMyDay).Select(t => t.MyDayOrder)) : -1,
            Order = NextOrder(Tasks.Where(t => t.ListId == listId).Select(t => t.Order)),
        };
        _db.Tasks.Insert(task);
        Tasks.Add(task); // keep the in-memory collection in sync for in-place refresh
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void UpdateTask(TaskItem task)
    {
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void DeleteTask(TaskItem task)
    {
        _db.Tasks.Delete(task.Id);
        _db.DeleteAttachmentsForTask(task.Id);   // local attachments die with the task (ADR-013)
        Tasks.Remove(task);
        if (SelectedTask?.Id == task.Id)
            SelectedTask = null;

        RefreshActiveTasks();
    }

    [RelayCommand]
    private void MoveTaskToList((TaskItem task, TaskList targetList) param)
    {
        param.task.ListId = param.targetList.Id;
        param.task.GroupId = null;
        param.task.Order = NextOrder(Tasks.Where(t => t.ListId == param.targetList.Id).Select(t => t.Order));
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void MoveTaskToGroup((TaskItem task, TaskGroup? group) param)
    {
        param.task.GroupId = param.group?.Id;
        // Append at the end of the target group (ungrouped = null) so the moved task
        // lands predictably, matching MoveTaskToList's next-order placement.
        param.task.Order = NextOrder(Tasks.Where(t => t.GroupId == param.group?.Id).Select(t => t.Order));
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);

        // Auto-expand the target group so the moved task is visible
        if (param.group != null && param.group.Collapsed)
        {
            param.group.Collapsed = false;
            _db.Groups.Update(param.group);
        }

        RefreshActiveTasks();
    }

    // ─── Closing System ───────────────────────────────────
    [RelayCommand]
    private void CloseTask((TaskItem task, CloseMode mode) param)
    {
        param.task.CloseRecord = new CloseRecord
        {
            ClosedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CloseMode = param.mode,
        };
        param.task.Completed = param.mode == CloseMode.Complete;
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCloseDisplay();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void ReopenTask(TaskItem task)
    {
        task.CloseRecord = null;
        task.Completed = false;
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        task.NotifyCloseDisplay();
        _db.Tasks.Update(task);
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void EditCloseTime((TaskItem task, long newTime) param)
    {
        if (param.task.CloseRecord == null) return;
        param.task.CloseRecord.ClosedAt = param.newTime;
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
    }

    // ─── My Day ───────────────────────────────────────────
    [RelayCommand]
    private void ToggleMyDay(TaskItem task)
    {
        if (task.IsMyDay)
        {
            task.IsMyDay = false;
            task.MyDayOrder = -1;
        }
        else
        {
            task.IsMyDay = true;
            task.MyDayOrder = NextOrder(Tasks.Where(t => t.IsMyDay).Select(t => t.MyDayOrder));
        }
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        RefreshActiveTasks();
    }

    // ─── Importance ───────────────────────────────────────
    [RelayCommand]
    private void ToggleImportant(TaskItem task)
    {
        task.IsImportant = !task.IsImportant;
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        RefreshActiveTasks();
    }

    // ─── Steps ────────────────────────────────────────────
    [RelayCommand]
    private void AddStep((TaskItem task, string title) param)
    {
        param.task.Steps.Add(new TaskStep
        {
            Title = param.title,
            Order = param.task.Steps.Count,
        });
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
    }

    /// <summary>Insert a new step after the given index and set it to editing mode</summary>
    public void InsertStepAfter(TaskItem task, int afterIndex)
    {
        for (int i = afterIndex + 1; i < task.Steps.Count; i++)
            task.Steps[i].Order++;
        task.Steps.Insert(afterIndex + 1, new TaskStep
        {
            Title = "",
            Order = afterIndex + 1,
            IsEditing = true
        });
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        task.NotifyCompletedStepCount();
        _db.Tasks.Update(task);
    }

    [RelayCommand]
    private void ToggleStep((TaskItem task, TaskStep step) param)
    {
        param.step.Completed = !param.step.Completed;
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
    }

    [RelayCommand]
    private void DeleteStep((TaskItem task, TaskStep step) param)
    {
        param.task.Steps.Remove(param.step);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
    }

    [RelayCommand]
    private void PromoteStepToTask((TaskItem task, TaskStep step) param)
    {
        var newTask = new TaskItem
        {
            Title = param.step.Title,
            ListId = param.task.ListId,
            Order = NextOrder(Tasks.Where(t => t.ListId == param.task.ListId).Select(t => t.Order)),
        };
        _db.Tasks.Insert(newTask);
        Tasks.Add(newTask); // keep the in-memory collection in sync for in-place refresh
        param.task.Steps.Remove(param.step);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
    }

    // ─── Tag Management ───────────────────────────────────
    [RelayCommand]
    private void CreateTag((string name, string color) param)
    {
        var name = param.name.Trim();
        // The tags collection has a unique index on Name: inserting a duplicate throws
        // a LiteException that would crash the app. Reject it up front instead.
        if (Tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            DiagnosticLog.Warn("tags", $"ignoring duplicate tag name '{name}'");
            return;
        }
        var tag = new Tag { Name = name, Color = param.color };
        _db.Tags.Insert(tag);
        LoadTags();
    }

    [RelayCommand]
    private void UpdateTag(Tag tag)
    {
        // Same unique-index guard as CreateTag, for a rename that collides with another tag.
        if (Tags.Any(t => t.Id != tag.Id && string.Equals(t.Name, tag.Name, StringComparison.OrdinalIgnoreCase)))
        {
            DiagnosticLog.Warn("tags", $"ignoring rename of '{tag.Id}' to duplicate name '{tag.Name}'");
            return;
        }
        _db.Tags.Update(tag);
        LoadTags();
    }

    [RelayCommand]
    private void DeleteTag(Tag tag)
    {
        // Remove from all tasks
        var affected = Tasks.Where(t => t.TagIds.Contains(tag.Id)).ToList();
        foreach (var t in affected)
        {
            t.TagIds.Remove(tag.Id);
            t.NotifyTagsChanged();
            _db.Tasks.Update(t);
        }
        _db.Tags.Delete(tag.Id);
        LoadTags();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void AddTagToTask((TaskItem task, Tag tag) param)
    {
        if (!param.task.TagIds.Contains(param.tag.Id))
        {
            param.task.TagIds.Add(param.tag.Id);
            param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            param.task.NotifyTagsChanged();
            _db.Tasks.Update(param.task);
            RefreshActiveTasks();
        }
    }

    [RelayCommand]
    private void RemoveTagFromTask((TaskItem task, Tag tag) param)
    {
        param.task.TagIds.Remove(param.tag.Id);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyTagsChanged();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
    }

    // ─── Dialog Toggles ───────────────────────────────────
    [RelayCommand]
    private void OpenTagDialog() => IsTagDialogOpen = true;

    [RelayCommand]
    private void CloseTagDialog() => IsTagDialogOpen = false;

    [RelayCommand]
    private void OpenDateTimeDialog(TaskItem task)
    {
        DateTimeTargetTask = task;
        IsDateTimeDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDateTimeDialog() => IsDateTimeDialogOpen = false;

    // ─── Theme ────────────────────────────────────────────
    [RelayCommand]
    private void ToggleTheme()
    {
        Theme = Theme == "Light" ? "Dark" : "Light";
        SettingsService.Current.Theme = Theme;
        SettingsService.Save();
        ThemeService.Apply(Theme);
    }

    // ─── Settings page ────────────────────────────────────
    [RelayCommand]
    private void OpenSettings() => IsSettingsMode = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsMode = false;
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
