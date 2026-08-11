using System;
using System.IO;
using System.Linq;
using ToDo.Models;
using ToDo.Services;
using ToDo.Sync;
using ToDo.ViewModels;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises MainViewModel's mutation commands — the CRUD users hit daily: creating /
/// deleting / moving tasks, closing + reopening, steps, and list / group / list-group
/// management. Seeded via the DB so the in-memory in-place refresh paths are exercised.
/// </summary>
[Collection("settings-shared")]   // SettingsService is a shared static — serialize with the other VM/service tests
public sealed class MainViewModelCommandsTests : IDisposable
{
    private readonly string _dir;
    private readonly DatabaseService _db;
    private readonly FakeClock _clock;
    private readonly MainViewModel _vm;

    private static readonly DateTime Today = new(2026, 8, 9);

    public MainViewModelCommandsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "todo-cmd-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        SettingsService.UseDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
        _clock = new FakeClock(Today);
        _vm = new MainViewModel(_db, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private TaskItem Task(string id) => _vm.Tasks.First(t => t.Id == id);

    private TaskList CustomList(string id, string name, int order)
    {
        var list = new TaskList { Id = id, Name = name, Type = ListType.Custom, Order = order };
        _db.Lists.Insert(list);
        return list;
    }

    // ─── Create / Update / Delete task ────────────────────

    [Fact]
    public void CreateTask_InSystemList_GoesToInbox_NotMyDay()
    {
        _vm.ActiveListId = "list-tasks";
        _vm.CreateTaskCommand.Execute("写周报");

        var t = _vm.Tasks.Single(x => x.Title == "写周报");
        Assert.Equal("list-tasks", t.ListId);
        Assert.False(t.IsMyDay);
        Assert.Equal(0, t.Order);                       // first task in an empty inbox
        Assert.NotNull(_db.Tasks.FindById(t.Id));       // persisted
    }

    [Fact]
    public void CreateTask_InCustomList_GoesToThatList()
    {
        CustomList("list-custom", "工作", 1);
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";

        _vm.CreateTaskCommand.Execute("交付");

        var t = _vm.Tasks.Single(x => x.Title == "交付");
        Assert.Equal("list-custom", t.ListId);
    }

    [Fact]
    public void CreateTask_InMyDay_SetsMyDayFlagAndOrder()
    {
        _vm.ActiveListId = "list-myday";
        _vm.CreateTaskCommand.Execute("晨跑");

        var t = _vm.Tasks.Single(x => x.Title == "晨跑");
        Assert.True(t.IsMyDay);
        Assert.Equal(0, t.MyDayOrder);                  // NextOrder of empty My Day
    }

    [Fact]
    public void CreateTask_WhileSearching_ForcesInbox()
    {
        CustomList("list-custom", "工作", 1);
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";
        _vm.SearchQuery = "abc";                        // searching → inbox regardless of active list

        _vm.CreateTaskCommand.Execute("草稿");

        var t = _vm.Tasks.Single(x => x.Title == "草稿");
        Assert.Equal("list-tasks", t.ListId);
        Assert.False(t.IsMyDay);
    }

    [Fact]
    public void CreateTask_NoActiveList_NoOp()
    {
        _vm.ActiveList = null;
        _vm.CreateTaskCommand.Execute("孤儿任务");

        Assert.Empty(_vm.Tasks);
    }

    [Fact]
    public void UpdateTask_PersistsTitle_AndShowsInActiveView()
    {
        _vm.CreateTaskCommand.Execute("旧标题");
        var t = Task(_vm.Tasks.Single(x => x.Title == "旧标题").Id);

        t.Title = "新标题";
        _vm.UpdateTaskCommand.Execute(t);

        Assert.Equal("新标题", _db.Tasks.FindById(t.Id)!.Title);
        Assert.Contains(t, _vm.ActiveTasks);
    }

    [Fact]
    public void DeleteTask_RemovesFromDbMemory_AndClearsSelection()
    {
        _vm.CreateTaskCommand.Execute("要删的");
        var t = _vm.Tasks.Single(x => x.Title == "要删的");
        _vm.SelectedTask = t;

        _vm.DeleteTaskCommand.Execute(t);

        Assert.Null(_db.Tasks.FindById(t.Id));
        Assert.DoesNotContain(t, _vm.Tasks);
        Assert.Null(_vm.SelectedTask);
    }

    // ─── Move between lists ───────────────────────────────

    [Fact]
    public void MoveTaskToList_ReassignsList_AppendsAtEnd()
    {
        var src = CustomList("list-a", "A", 1);
        var dst = CustomList("list-b", "B", 2);
        _db.Tasks.Insert(new TaskItem { Id = "t1", ListId = "list-a", Title = "move", Order = 0 });
        _db.Tasks.Insert(new TaskItem { Id = "t2", ListId = "list-b", Title = "existing", Order = 0 });
        _vm.Refresh();
        _vm.ActiveListId = "list-b";

        _vm.MoveTaskToListCommand.Execute((_vm.Tasks.First(t => t.Id == "t1"), dst));

        var moved = _vm.Tasks.First(t => t.Id == "t1");
        Assert.Equal("list-b", moved.ListId);
        Assert.Null(moved.GroupId);
        Assert.Equal(1, moved.Order);   // after list-b's current max (t2 = 0)
        Assert.Equal(new[] { "t2", "t1" }, _vm.ActiveTasks.Select(t => t.Id));
    }

    // ─── Close / reopen ───────────────────────────────────

    [Fact]
    public void CloseTask_PlainTask_ClosesWithoutGenerating()
    {
        _vm.CreateTaskCommand.Execute("一次性");
        var t = Task(_vm.Tasks.Single(x => x.Title == "一次性").Id);

        _vm.CloseTaskCommand.Execute((t, CloseMode.Complete, false));

        Assert.True(t.IsClosed);
        Assert.Equal(CloseMode.Complete, t.CloseRecord!.CloseMode);
        Assert.Single(_vm.Tasks);               // no recurring next-instance generated
        Assert.Contains(t, _vm.CompletedTasks); // shows under the completed section
    }

    [Fact]
    public void ReopenTask_RestoresOpenState()
    {
        _vm.CreateTaskCommand.Execute("完成又取消");
        var t = Task(_vm.Tasks.Single(x => x.Title == "完成又取消").Id);
        _vm.CloseTaskCommand.Execute((t, CloseMode.Complete, false));

        _vm.ReopenTaskCommand.Execute(t);

        Assert.False(t.IsClosed);
        Assert.Null(t.CloseRecord);
        Assert.Contains(t, _vm.ActiveTasks);
    }

    [Fact]
    public void EditCloseTime_UpdatesClosedAt()
    {
        _vm.CreateTaskCommand.Execute("补记完成时间");
        var t = Task(_vm.Tasks.Single(x => x.Title == "补记完成时间").Id);
        _vm.CloseTaskCommand.Execute((t, CloseMode.Complete, false));

        _vm.EditCloseTimeCommand.Execute((t, 1_700_000_000_000L));

        Assert.Equal(1_700_000_000_000L, t.CloseRecord!.ClosedAt);
        Assert.Equal(1_700_000_000_000L, _db.Tasks.FindById(t.Id)!.CloseRecord!.ClosedAt);
    }

    [Fact]
    public void EditCloseTime_WithoutCloseRecord_NoOp()
    {
        _vm.CreateTaskCommand.Execute("未完成");
        var t = Task(_vm.Tasks.Single(x => x.Title == "未完成").Id);

        _vm.EditCloseTimeCommand.Execute((t, 1L));

        Assert.Null(t.CloseRecord);
    }

    // ─── My Day / Important toggles ───────────────────────

    [Fact]
    public void ToggleMyDay_AddsThenRemoves()
    {
        _vm.CreateTaskCommand.Execute("我的一天");
        var t = Task(_vm.Tasks.Single(x => x.Title == "我的一天").Id);

        _vm.ToggleMyDayCommand.Execute(t);
        Assert.True(t.IsMyDay);
        Assert.Equal(0, t.MyDayOrder);

        _vm.ToggleMyDayCommand.Execute(t);
        Assert.False(t.IsMyDay);
        Assert.Equal(-1, t.MyDayOrder);
    }

    [Fact]
    public void ToggleImportant_FlipsFlag()
    {
        _vm.CreateTaskCommand.Execute("重要的事");
        var t = Task(_vm.Tasks.Single(x => x.Title == "重要的事").Id);

        _vm.ToggleImportantCommand.Execute(t);
        Assert.True(t.IsImportant);

        _vm.ToggleImportantCommand.Execute(t);
        Assert.False(t.IsImportant);
    }

    // ─── Steps ────────────────────────────────────────────

    [Fact]
    public void AddStep_AppendsToTask()
    {
        _vm.CreateTaskCommand.Execute("带步骤");
        var t = Task(_vm.Tasks.Single(x => x.Title == "带步骤").Id);

        _vm.AddStepCommand.Execute((t, "第一步"));
        _vm.AddStepCommand.Execute((t, "第二步"));

        Assert.Equal(new[] { "第一步", "第二步" }, t.Steps.Select(s => s.Title));
        Assert.Equal(new[] { 0, 1 }, t.Steps.Select(s => s.Order));
    }

    [Fact]
    public void InsertStepAfter_ShiftsOrdersAndEntersEditing()
    {
        _vm.CreateTaskCommand.Execute("插步骤");
        var t = Task(_vm.Tasks.Single(x => x.Title == "插步骤").Id);
        _vm.AddStepCommand.Execute((t, "A"));
        _vm.AddStepCommand.Execute((t, "B"));

        _vm.InsertStepAfter(t, 0);   // insert between A and B

        Assert.Equal(new[] { "A", "", "B" }, t.Steps.Select(s => s.Title));
        Assert.Equal(new[] { 0, 1, 2 }, t.Steps.Select(s => s.Order));
        Assert.True(t.Steps[1].IsEditing);
    }

    [Fact]
    public void ToggleStep_FlipsCompleted_AndUpdatesCount()
    {
        _vm.CreateTaskCommand.Execute("勾步骤");
        var t = Task(_vm.Tasks.Single(x => x.Title == "勾步骤").Id);
        _vm.AddStepCommand.Execute((t, "A"));
        var step = t.Steps[0];

        _vm.ToggleStepCommand.Execute((t, step));
        Assert.True(step.Completed);
        Assert.Equal(1, t.CompletedStepCount);

        _vm.ToggleStepCommand.Execute((t, step));
        Assert.False(step.Completed);
        Assert.Equal(0, t.CompletedStepCount);
    }

    [Fact]
    public void DeleteStep_RemovesStep()
    {
        _vm.CreateTaskCommand.Execute("删步骤");
        var t = Task(_vm.Tasks.Single(x => x.Title == "删步骤").Id);
        _vm.AddStepCommand.Execute((t, "A"));
        _vm.AddStepCommand.Execute((t, "B"));

        _vm.DeleteStepCommand.Execute((t, t.Steps[0]));

        Assert.Single(t.Steps);
        Assert.Equal("B", t.Steps[0].Title);
    }

    [Fact]
    public void PromoteStepToTask_CreatesTask_AndRemovesStep()
    {
        var custom = CustomList("list-custom", "工作", 1);
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";
        _vm.CreateTaskCommand.Execute("父任务");
        var t = Task(_vm.Tasks.Single(x => x.Title == "父任务").Id);
        _vm.AddStepCommand.Execute((t, "升级为任务"));

        _vm.PromoteStepToTaskCommand.Execute((t, t.Steps[0]));

        var promoted = _vm.Tasks.Single(x => x.Title == "升级为任务");
        Assert.Equal("list-custom", promoted.ListId);
        Assert.Empty(t.Steps);
        Assert.Equal("升级为任务", _db.Tasks.FindById(promoted.Id)!.Title);
    }

    // ─── Lists ────────────────────────────────────────────

    [Fact]
    public void CreateList_AddsCustomList_AndSetsLastCreated()
    {
        _vm.CreateListCommand.Execute("新列表");

        var list = _vm.CustomLists.Single(x => x.Name == "新列表");
        Assert.Equal(ListType.Custom, list.Type);
        Assert.Equal("📋", list.Icon);
        Assert.Equal(list.Id, _vm.LastCreatedListId);
        Assert.NotNull(_db.Lists.FindById(list.Id));
    }

    [Fact]
    public void RenameList_PersistsName()
    {
        CustomList("list-custom", "旧名", 1);
        _vm.Refresh();
        var list = _vm.CustomLists.Single(x => x.Id == "list-custom");

        list.Name = "新名";
        _vm.RenameListCommand.Execute(list);

        Assert.Equal("新名", _db.Lists.FindById("list-custom")!.Name);
        Assert.Equal("新名", _vm.CustomLists.Single(x => x.Id == "list-custom").Name);
    }

    [Fact]
    public void DeleteList_MovesTasksToInbox_DeletesGroups_AndRepointsActiveList()
    {
        var custom = CustomList("list-custom", "要删的", 1);
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "list-custom", Name = "G", Order = 0 });
        _db.Tasks.Insert(new TaskItem { Id = "t1", ListId = "list-custom", GroupId = "g1", Title = "保住", Order = 0 });
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";

        _vm.DeleteListCommand.Execute(custom);

        Assert.Null(_db.Lists.FindById("list-custom"));
        Assert.Empty(_db.Groups.Find(g => g.ListId == "list-custom"));
        var moved = _db.Tasks.FindById("t1")!;
        Assert.Equal("list-tasks", moved.ListId);       // data preserved, not deleted
        Assert.Null(moved.GroupId);
        Assert.Equal("list-tasks", _vm.ActiveListId);   // active list re-pointed to inbox
    }

    [Fact]
    public void DeleteList_SystemList_NoOp()
    {
        var sys = _vm.Lists.First(l => l.Id == "list-tasks");
        _vm.DeleteListCommand.Execute(sys);

        Assert.NotNull(_db.Lists.FindById("list-tasks"));
    }

    // ─── List groups ──────────────────────────────────────

    [Fact]
    public void CreateListGroup_AddsGroup()
    {
        _vm.CreateListGroupCommand.Execute("家庭");

        var g = _vm.ListGroups.Single(x => x.Name == "家庭");
        Assert.NotNull(_db.ListGroups.FindById(g.Id));
        Assert.Equal(0, g.Order);   // first group
    }

    [Fact]
    public void RenameListGroup_PersistsName()
    {
        _vm.CreateListGroupCommand.Execute("旧组名");
        var g = _vm.ListGroups.Single(x => x.Name == "旧组名");

        g.Name = "新组名";
        _vm.RenameListGroupCommand.Execute(g);

        Assert.Equal("新组名", _db.ListGroups.FindById(g.Id)!.Name);
    }

    [Fact]
    public void DeleteListGroup_MovesListsToUngrouped()
    {
        _vm.CreateListGroupCommand.Execute("工作");
        var g = _vm.ListGroups.Single(x => x.Name == "工作");
        var list = CustomList("list-a", "A", 1);
        list.GroupId = g.Id;
        _db.Lists.Update(list);
        _vm.Refresh();

        _vm.DeleteListGroupCommand.Execute(g);

        Assert.Null(_db.ListGroups.FindById(g.Id));
        Assert.Null(_db.Lists.FindById("list-a")!.GroupId);
        Assert.Contains(_vm.UngroupedCustomLists, l => l.Id == "list-a");
    }

    [Fact]
    public void ToggleListGroupCollapse_FlipsAndPersists()
    {
        _vm.CreateListGroupCommand.Execute("分组");
        var g = _vm.ListGroups.Single(x => x.Name == "分组");

        _vm.ToggleListGroupCollapseCommand.Execute(g);
        Assert.True(g.Collapsed);
        Assert.True(_db.ListGroups.FindById(g.Id)!.Collapsed);

        _vm.ToggleListGroupCollapseCommand.Execute(g);
        Assert.False(g.Collapsed);
    }

    [Fact]
    public void MoveListToGroup_AssignsAndAutoExpands()
    {
        _vm.CreateListGroupCommand.Execute("分组");
        var g = _vm.ListGroups.Single(x => x.Name == "分组");
        g.Collapsed = true;
        _db.ListGroups.Update(g);
        var list = CustomList("list-a", "A", 1);
        _vm.Refresh();

        _vm.MoveListToGroupCommand.Execute((_vm.CustomLists.First(l => l.Id == "list-a"), g));

        Assert.Equal(g.Id, _db.Lists.FindById("list-a")!.GroupId);
        Assert.False(_db.ListGroups.FindById(g.Id)!.Collapsed);   // moved task's group auto-expanded
        Assert.Contains(_vm.GroupedCustomLists, d => d.Group.Id == g.Id && d.Lists.Any(l => l.Id == "list-a"));
    }

    [Fact]
    public void MoveListToGroup_NullGroup_UngroupsList()
    {
        _vm.CreateListGroupCommand.Execute("分组");
        var g = _vm.ListGroups.Single(x => x.Name == "分组");
        var list = CustomList("list-a", "A", 1);
        list.GroupId = g.Id;
        _db.Lists.Update(list);
        _vm.Refresh();

        _vm.MoveListToGroupCommand.Execute((_vm.CustomLists.First(l => l.Id == "list-a"), null));

        Assert.Null(_db.Lists.FindById("list-a")!.GroupId);
        Assert.Contains(_vm.UngroupedCustomLists, l => l.Id == "list-a");
    }

    // ─── Task groups ──────────────────────────────────────

    [Fact]
    public void CreateGroup_AddsToActiveList()
    {
        var custom = CustomList("list-custom", "工作", 1);
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";

        _vm.CreateGroupCommand.Execute("启动中");

        var g = _vm.Groups.Single(x => x.Name == "启动中");
        Assert.Equal("list-custom", g.ListId);
        Assert.NotNull(_db.Groups.FindById(g.Id));
    }

    [Fact]
    public void RenameGroup_PersistsName()
    {
        var custom = CustomList("list-custom", "工作", 1);
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "list-custom", Name = "旧组", Order = 0 });
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";
        var g = _vm.Groups.Single(x => x.Id == "g1");

        g.Name = "新组";
        _vm.RenameGroupCommand.Execute(g);

        Assert.Equal("新组", _db.Groups.FindById("g1")!.Name);
    }

    [Fact]
    public void DeleteGroup_MovesTasksToUngrouped()
    {
        var custom = CustomList("list-custom", "工作", 1);
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "list-custom", Name = "G", Order = 0 });
        _db.Tasks.Insert(new TaskItem { Id = "t1", ListId = "list-custom", GroupId = "g1", Title = "t", Order = 0 });
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";

        _vm.DeleteGroupCommand.Execute(_vm.Groups.Single(g => g.Id == "g1"));

        Assert.Null(_db.Groups.FindById("g1"));
        Assert.Null(_db.Tasks.FindById("t1")!.GroupId);
        Assert.Contains(_vm.GroupedTaskList[0].Tasks, t => t.Id == "t1");   // lands in ungrouped section
    }

    [Fact]
    public void ToggleGroupCollapse_FlipsAndPersists()
    {
        var custom = CustomList("list-custom", "工作", 1);
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "list-custom", Name = "G", Order = 0 });
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";
        var g = _vm.Groups.Single(x => x.Id == "g1");

        _vm.ToggleGroupCollapseCommand.Execute(g);
        Assert.True(g.Collapsed);
        Assert.True(_db.Groups.FindById("g1")!.Collapsed);

        _vm.ToggleGroupCollapseCommand.Execute(g);
        Assert.False(g.Collapsed);
    }

    // ─── Active-view list-type branches ───────────────────

    [Fact]
    public void RefreshActiveTasks_ImportantList_FiltersImportantOpenTasks()
    {
        _db.Tasks.Insert(new TaskItem { Id = "i1", ListId = "list-x", Title = "imp", IsImportant = true });
        _db.Tasks.Insert(new TaskItem { Id = "n1", ListId = "list-x", Title = "plain" });
        _vm.Refresh();
        _vm.ActiveListId = "list-important";

        Assert.Equal(new[] { "i1" }, _vm.ActiveTasks.Select(t => t.Id));
    }

    [Fact]
    public void RefreshActiveTasks_PlannedList_FiltersDueOrReminderTasks()
    {
        _db.Tasks.Insert(new TaskItem { Id = "d1", ListId = "list-x", Title = "due", DueDate = Ts(Today.AddDays(3)) });
        _db.Tasks.Insert(new TaskItem { Id = "r1", ListId = "list-x", Title = "rem", Reminder = Ts(Today.AddHours(2)) });
        _db.Tasks.Insert(new TaskItem { Id = "n1", ListId = "list-x", Title = "plain" });
        _vm.Refresh();
        _vm.ActiveListId = "list-planned";

        Assert.Equal(new[] { "d1", "r1" }, _vm.ActiveTasks.Select(t => t.Id));
        Assert.Equal(new[] { "d1", "r1" }, _vm.ActiveTasks.OrderBy(t => t.DueDate ?? long.MaxValue).Select(t => t.Id));
    }

    [Fact]
    public void RefreshActiveTasks_TasksList_SortsByModifiedAtDesc()
    {
        // Seed via ApplySync so ModifiedAt is pinned exactly — a tracked Insert auto-stamps it.
        _db.ApplySync(new[] { Change(new TaskItem { Id = "old", ListId = "list-tasks", Title = "old", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "new", ListId = "list-tasks", Title = "new", ModifiedAt = 300 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "mid", ListId = "list-tasks", Title = "mid", ModifiedAt = 200 }) });
        _vm.Refresh();
        _vm.ActiveListId = "list-tasks";

        Assert.Equal(new[] { "new", "mid", "old" }, _vm.ActiveTasks.Select(t => t.Id));
    }

    // ─── Undo bar (v1.3.2): 完成/删除 push a single-slot undo; Undo reopens the task
    // (deleting any generated recurring next-instance) or restores the deleted task +
    // attachments. ────────────────────────────────────────────

    [Fact]
    public void CloseTask_Complete_PushesUndo()
    {
        _vm.CreateTaskCommand.Execute("一次性");
        var t = _vm.Tasks.Single(x => x.Title == "一次性");

        _vm.CloseTaskCommand.Execute((t, CloseMode.Complete, false));

        Assert.NotNull(_vm.CurrentUndo);
        Assert.Contains("一次性", _vm.CurrentUndo.Message);
    }

    [Fact]
    public void CloseTask_Cancel_DoesNotPushUndo()
    {
        _vm.CreateTaskCommand.Execute("取消");
        var t = _vm.Tasks.Single(x => x.Title == "取消");

        _vm.CloseTaskCommand.Execute((t, CloseMode.Cancel, false));
        Assert.Null(_vm.CurrentUndo);   // plain cancel

        _vm.CreateTaskCommand.Execute("停掉系列");
        var r = _vm.Tasks.Single(x => x.Title == "停掉系列");
        _vm.CloseTaskCommand.Execute((r, CloseMode.Cancel, true));
        Assert.Null(_vm.CurrentUndo);   // cancel-the-series
    }

    [Fact]
    public void Undo_AfterComplete_ReopensTask()
    {
        _vm.CreateTaskCommand.Execute("再开");
        var t = _vm.Tasks.Single(x => x.Title == "再开");
        _vm.CloseTaskCommand.Execute((t, CloseMode.Complete, false));
        Assert.True(t.IsClosed);

        _vm.UndoCommand.Execute(null);

        Assert.False(t.IsClosed);
        Assert.Null(t.CloseRecord);
        Assert.Contains(t, _vm.ActiveTasks);
        Assert.Null(_vm.CurrentUndo);   // slot consumed by the undo itself
    }

    [Fact]
    public void Undo_WhenNoPendingUndo_NoOp()
    {
        _vm.UndoCommand.Execute(null);   // must not throw
        Assert.Null(_vm.CurrentUndo);
    }

    [Fact]
    public void NewOperation_ReplacesPendingUndo()
    {
        _vm.CreateTaskCommand.Execute("A");
        _vm.CreateTaskCommand.Execute("B");
        var a = _vm.Tasks.Single(x => x.Title == "A");
        var b = _vm.Tasks.Single(x => x.Title == "B");

        _vm.CloseTaskCommand.Execute((a, CloseMode.Complete, false));
        var firstUndo = _vm.CurrentUndo;
        Assert.NotNull(firstUndo);

        _vm.CloseTaskCommand.Execute((b, CloseMode.Complete, false));
        Assert.NotSame(firstUndo, _vm.CurrentUndo);   // newest wins

        _vm.UndoCommand.Execute(null);
        Assert.False(b.IsClosed);   // B reopened
        Assert.True(a.IsClosed);    // A untouched
    }

    [Fact]
    public void CloseTask_CompleteRecurring_Undo_RemovesGeneratedNext()
    {
        _db.Tasks.Insert(new TaskItem
        {
            Id = "r1", Title = "喝水", ListId = "list-tasks",
            Recurrence = RecurrenceFrequency.Daily, DueDate = Ts(Today),
        });
        _vm.Refresh();
        var root = _vm.Tasks.First(t => t.Id == "r1");

        _vm.CloseTaskCommand.Execute((root, CloseMode.Complete, false));

        var generated = _vm.Tasks.Single(t => t.RecurrenceSeriesId == "r1");
        Assert.Equal(2, _vm.Tasks.Count);

        _vm.UndoCommand.Execute(null);

        Assert.False(root.IsClosed);
        Assert.Null(root.CloseRecord);
        Assert.Single(_vm.Tasks);                            // generated instance removed
        Assert.Null(_db.Tasks.FindById(generated.Id));
    }

    [Fact]
    public void DeleteTask_PushesUndo_AndUndo_RestoresTask()
    {
        CustomList("list-custom", "工作", 1);
        _db.Tasks.Insert(new TaskItem { Id = "t1", ListId = "list-custom", Title = "恢复", Order = 3 });
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";
        var t = _vm.Tasks.First(x => x.Id == "t1");
        _vm.SelectedTask = t;

        _vm.DeleteTaskCommand.Execute(t);

        Assert.Null(_db.Tasks.FindById("t1"));
        Assert.Null(_vm.SelectedTask);
        Assert.NotNull(_vm.CurrentUndo);

        _vm.UndoCommand.Execute(null);

        var restored = _db.Tasks.FindById("t1");
        Assert.NotNull(restored);
        Assert.Equal("恢复", restored.Title);
        Assert.Equal("list-custom", restored.ListId);   // list / order preserved → lands back in place
        Assert.Equal(3, restored.Order);
        Assert.Contains(_vm.Tasks, x => x.Id == "t1");
        Assert.Null(_vm.SelectedTask);   // undo does not re-select
    }

    [Fact]
    public void DeleteTask_WithAttachments_Undo_RestoresAttachments()
    {
        _vm.CreateTaskCommand.Execute("带附件");
        var t = _vm.Tasks.Single(x => x.Title == "带附件");
        _db.AddAttachment(new TaskAttachment
        {
            Id = "att1", TaskId = t.Id, FileName = "a.txt", Size = 3, Data = new byte[] { 1, 2, 3 },
        });
        _db.AddAttachment(new TaskAttachment
        {
            Id = "att2", TaskId = t.Id, FileName = "b.bin", Size = 2, Data = new byte[] { 9, 8 },
        });

        _vm.DeleteTaskCommand.Execute(t);
        Assert.Empty(_db.GetAttachments(t.Id));

        _vm.UndoCommand.Execute(null);

        var restored = _db.GetAttachments(t.Id);
        Assert.Equal(new[] { "a.txt", "b.bin" }, restored.OrderBy(a => a.Id).Select(a => a.FileName));
        Assert.Equal(new byte[] { 1, 2, 3 }, restored.Single(a => a.Id == "att1").Data);   // original bytes back
    }

    private static long Ts(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    private static SyncChange Change(object entity) => SyncEntitySerializer.ToChange(entity)!;

    private sealed class FakeClock : IClock
    {
        public DateTime Today { get; }
        public DateTimeOffset UtcNow { get; }

        public FakeClock(DateTime today)
        {
            Today = today.Date;
            UtcNow = new DateTimeOffset(today.Date, TimeSpan.Zero);
        }
    }
}
