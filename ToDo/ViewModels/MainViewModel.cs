using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToDo.Models;
using ToDo.Services;

namespace ToDo.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;

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

    // ─── Theme ────────────────────────────────────────────
    [ObservableProperty]
    private string _theme = "Light"; // Light, Dark

    // ─── Dialog state ─────────────────────────────────────
    [ObservableProperty]
    private bool _isTagDialogOpen;

    [ObservableProperty]
    private bool _isDateTimeDialogOpen;

    [ObservableProperty]
    private TaskItem? _dateTimeTargetTask;

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

    private static bool IsToday(long ts)
    {
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
        return dt.Date == DateTime.Today;
    }

    public MainViewModel(DatabaseService db)
    {
        _db = db;
        LoadAll();
        DailyMyDayReset();
    }

    /// <summary>Remove undone yesterday tasks from My Day</summary>
    private void DailyMyDayReset()
    {
        var today = DateTime.Today;
        var yesterdayTasks = Tasks.Where(t =>
            t.IsMyDay && t.CloseRecord == null && t.DueDate != null
            && !IsToday(t.DueDate.Value)
            && DateTimeOffset.FromUnixTimeMilliseconds(t.DueDate.Value).LocalDateTime.Date < today
        ).ToList();

        foreach (var t in yesterdayTasks)
        {
            t.IsMyDay = false;
            t.MyDayOrder = -1;
            t.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
            t.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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

        var ungrouped = CustomLists.Where(l => l.GroupId == null).ToList();
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
    private static int CountForList(TaskList list, IEnumerable<TaskItem> tasks)
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

        // Update sidebar counts in real time
        foreach (var l in Lists)
            l.TaskCount = CountForList(l, Tasks);

        var isSearching = !string.IsNullOrWhiteSpace(SearchQuery);

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
        LoadTasks();
        RefreshActiveTasks();
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
        LoadTasks();
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
        
        LoadTasks();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void ToggleGroupCollapse(TaskGroup group)
    {
        group.Collapsed = !group.Collapsed;
        _db.Groups.Update(group);
        LoadGroups();
        LoadTasks();
        RefreshActiveTasks();
    }

    // ─── Task Commands ────────────────────────────────────
    [RelayCommand]
    private void CreateTask(string title)
    {
        if (ActiveList == null) return;

        // System lists → tasks go to inbox ("list-tasks"); custom lists → to that list
        var listId = ActiveList.Type == ListType.Custom ? ActiveList.Id : "list-tasks";
        var isMyDay = ActiveList.Type == ListType.MyDay;

        var task = new TaskItem
        {
            Title = title,
            ListId = listId,
            IsMyDay = isMyDay,
            MyDayOrder = isMyDay ? NextOrder(Tasks.Where(t => t.IsMyDay).Select(t => t.MyDayOrder)) : -1,
            Order = NextOrder(Tasks.Where(t => t.ListId == listId).Select(t => t.Order)),
        };
        _db.Tasks.Insert(task);
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void UpdateTask(TaskItem task)
    {
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        
        
        LoadTasks();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void DeleteTask(TaskItem task)
    {
        _db.Tasks.Delete(task.Id);
        if (SelectedTask?.Id == task.Id)
            SelectedTask = null;
        
        LoadTasks();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void MoveTaskToList((TaskItem task, TaskList targetList) param)
    {
        param.task.ListId = param.targetList.Id;
        param.task.GroupId = null;
        param.task.Order = Tasks.Count(t => t.ListId == param.targetList.Id);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);
        
        LoadTasks();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void MoveTaskToGroup((TaskItem task, TaskGroup? group) param)
    {
        param.task.GroupId = param.group?.Id;
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);
        
        LoadTasks();
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
        _db.Tasks.Update(param.task);
        
        LoadTasks();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void ReopenTask(TaskItem task)
    {
        task.CloseRecord = null;
        task.Completed = false;
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        
        LoadTasks();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void EditCloseTime((TaskItem task, long newTime) param)
    {
        if (param.task.CloseRecord == null) return;
        param.task.CloseRecord.ClosedAt = param.newTime;
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);
        
        LoadTasks();
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
        
        LoadTasks();
        RefreshActiveTasks();
    }

    // ─── Importance ───────────────────────────────────────
    [RelayCommand]
    private void ToggleImportant(TaskItem task)
    {
        task.IsImportant = !task.IsImportant;
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        
        LoadTasks();
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
        param.task.Steps.Remove(param.step);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
        LoadTasks();
        RefreshActiveTasks();
    }

    // ─── Tag Management ───────────────────────────────────
    [RelayCommand]
    private void CreateTag((string name, string color) param)
    {
        var tag = new Tag { Name = param.name, Color = param.color };
        _db.Tags.Insert(tag);
        LoadTags();
    }

    [RelayCommand]
    private void UpdateTag(Tag tag)
    {
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
            _db.Tasks.Update(t);
        }
        _db.Tags.Delete(tag.Id);
        LoadTags();
        
        LoadTasks();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void AddTagToTask((TaskItem task, Tag tag) param)
    {
        if (!param.task.TagIds.Contains(param.tag.Id))
        {
            param.task.TagIds.Add(param.tag.Id);
            param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _db.Tasks.Update(param.task);
            
            LoadTasks();
        RefreshActiveTasks();
        }
    }

    [RelayCommand]
    private void RemoveTagFromTask((TaskItem task, Tag tag) param)
    {
        param.task.TagIds.Remove(param.tag.Id);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);
        
        LoadTasks();
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
}
