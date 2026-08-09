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
/// Exercises MainViewModel's derived-data logic — the parts users hit every day:
/// RefreshActiveTasks filtering/sorting/grouping, sidebar counts (CountForList) and
/// the My Day reset. A fake clock pins "today" so date boundaries are deterministic.
/// </summary>
[Collection("settings-shared")]   // serialized with SettingsServiceTests/SyncServiceTests — SettingsService is a shared static
public sealed class MainViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly DatabaseService _db;
    private readonly FakeClock _clock;
    private readonly MainViewModel _vm;

    /// <summary>Fixed local "today" so due-date boundaries never drift with the real date.</summary>
    private static readonly DateTime Today = new(2026, 8, 9);

    public MainViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "todo-tests-" + Guid.NewGuid().ToString("N"));
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

    private static long Ts(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    private static SyncChange Change(object entity) => SyncEntitySerializer.ToChange(entity)!;

    private TaskItem Task(string id) => _vm.Tasks.First(t => t.Id == id);

    private int CountFor(string listId) => _vm.Lists.First(l => l.Id == listId).TaskCount;

    private void SeedTask(string id, string listId = "list-tasks", bool myDay = false,
        int myDayOrder = -1, DateTime? due = null, bool closed = false) =>
        _db.Tasks.Insert(new TaskItem
        {
            Id = id,
            Title = id,
            ListId = listId,
            IsMyDay = myDay,
            MyDayOrder = myDayOrder,
            DueDate = due.HasValue ? Ts(due.Value) : null,
            CloseRecord = closed ? new CloseRecord { ClosedAt = Ts(Today) } : null,
        });

    [Fact]
    public void RefreshActiveTasks_MyDay_IncludesDueTodayAndSortsByMyDayOrder()
    {
        SeedTask("t4", "list-myday", myDay: false, due: Today);
        SeedTask("t2", "list-myday", myDay: true, myDayOrder: 0, due: Today);
        SeedTask("t1", "list-myday", myDay: true, myDayOrder: 1, due: Today);
        SeedTask("t3", "list-myday", myDay: true, myDayOrder: 2, due: Today);
        _vm.Refresh();
        _vm.ActiveListId = "list-myday";

        // A due-today task without the My Day flag still appears (auto-add target),
        // sorting by MyDayOrder (-1) ahead of the flagged ones.
        Assert.Equal(new[] { "t4", "t2", "t1", "t3" }, _vm.ActiveTasks.Select(t => t.Id));
        Assert.All(_vm.ActiveTasks, t => Assert.False(t.IsClosed));
    }

    [Fact]
    public void RefreshActiveTasks_Search_MatchesAcrossLists_AndSortsByModifiedAtDesc()
    {
        // Seeds via ApplySync so ModifiedAt is pinned exactly (search sorts by it).
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-a", Title = "Buy milk", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t2", ListId = "list-b", Title = "buy bread", ModifiedAt = 200 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t3", ListId = "list-c", Title = "Milk the cow", Note = "remember to buy feed", ModifiedAt = 300 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t5", ListId = "list-d", Title = "buy done", CloseRecord = new CloseRecord { ClosedAt = 400 }, ModifiedAt = 400 }) });
        _vm.Refresh();
        _vm.SearchQuery = "buy";

        // Matches title OR note, across all lists, newest first; closed matches land separately.
        Assert.Equal(new[] { "t3", "t2", "t1" }, _vm.ActiveTasks.Select(t => t.Id));
        Assert.Equal(new[] { "t5" }, _vm.CompletedTasks.Select(t => t.Id));
    }

    [Fact]
    public void RefreshActiveTasks_CustomList_BuildsGroupedSections()
    {
        _db.Lists.Insert(new TaskList { Id = "list-custom", Name = "Custom", Type = ListType.Custom, Order = 1 });
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "list-custom", Name = "Group 1", Order = 0 });
        _db.Groups.Insert(new TaskGroup { Id = "g2", ListId = "list-custom", Name = "Group 2", Order = 1 });
        _db.Tasks.Insert(new TaskItem { Id = "u1", ListId = "list-custom", Title = "ungrouped", Order = 0 });
        _db.Tasks.Insert(new TaskItem { Id = "a1", ListId = "list-custom", GroupId = "g1", Title = "a1", Order = 0 });
        _db.Tasks.Insert(new TaskItem { Id = "a2", ListId = "list-custom", GroupId = "g1", Title = "a2", Order = 1 });
        _db.Tasks.Insert(new TaskItem { Id = "b1", ListId = "list-custom", GroupId = "g2", Title = "b1", Order = 0 });
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";

        // Ungrouped section first (always a drop target), then groups in Order.
        Assert.Equal(3, _vm.GroupedTaskList.Count);

        var ungrouped = _vm.GroupedTaskList[0];
        Assert.False(ungrouped.HasGroup);
        Assert.False(ungrouped.ShowEmptyUngroupedHint);   // has a task → no drop hint
        Assert.Equal(new[] { "u1" }, ungrouped.Tasks.Select(t => t.Id));

        var g1 = _vm.GroupedTaskList[1];
        Assert.Equal("g1", g1.Group!.Id);
        Assert.Equal(new[] { "a1", "a2" }, g1.Tasks.Select(t => t.Id));

        var g2 = _vm.GroupedTaskList[2];
        Assert.Equal("g2", g2.Group!.Id);
        Assert.Equal(new[] { "b1" }, g2.Tasks.Select(t => t.Id));
    }

    [Fact]
    public void RefreshActiveTasks_AllTasksGrouped_UngroupedSectionShowsDropHint()
    {
        // No ungrouped tasks at all → the ungrouped section is empty and must still
        // advertise itself as a drop target for grouped tasks.
        _db.Lists.Insert(new TaskList { Id = "list-custom", Name = "Custom", Type = ListType.Custom, Order = 1 });
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "list-custom", Name = "G", Order = 0 });
        _db.Tasks.Insert(new TaskItem { Id = "a1", ListId = "list-custom", GroupId = "g1", Title = "a1", Order = 0 });
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";

        var ungrouped = _vm.GroupedTaskList[0];
        Assert.False(ungrouped.HasGroup);
        Assert.Empty(ungrouped.Tasks);
        Assert.True(ungrouped.ShowEmptyUngroupedHint);
    }

    [Fact]
    public void MoveTaskToGroup_AppendsAtEndOfTargetGroup()
    {
        _db.Lists.Insert(new TaskList { Id = "list-custom", Name = "Custom", Type = ListType.Custom, Order = 1 });
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "list-custom", Name = "G", Order = 0 });
        var t1 = new TaskItem { Id = "t1", ListId = "list-custom", GroupId = "g1", Title = "a", Order = 0 };
        var t2 = new TaskItem { Id = "t2", ListId = "list-custom", GroupId = "g1", Title = "b", Order = 1 };
        var u1 = new TaskItem { Id = "u1", ListId = "list-custom", Title = "u", Order = 0 };
        _db.Tasks.Insert(t1);
        _db.Tasks.Insert(t2);
        _db.Tasks.Insert(u1);
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";

        // Use the instance from _vm.Tasks — LiteDB hands back a fresh object on query,
        // so mutating the local variable wouldn't affect what the ViewModel sees.
        _vm.MoveTaskToGroupCommand.Execute((_vm.Tasks.First(t => t.Id == "u1"), _vm.Groups.First(g => g.Id == "g1")));

        var moved = _vm.Tasks.First(t => t.Id == "u1");
        Assert.Equal("g1", moved.GroupId);
        Assert.Equal(2, moved.Order);   // appended after the group's current max (t2 = 1)
    }

    [Fact]
    public void MoveTaskToGroup_NullGroup_UngroupsTask_AppendedLast()
    {
        _db.Lists.Insert(new TaskList { Id = "list-custom", Name = "Custom", Type = ListType.Custom, Order = 1 });
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "list-custom", Name = "G", Order = 0 });
        var a1 = new TaskItem { Id = "a1", ListId = "list-custom", GroupId = "g1", Title = "a1", Order = 0 };
        var u1 = new TaskItem { Id = "u1", ListId = "list-custom", Title = "u1", Order = 0 };
        _db.Tasks.Insert(a1);
        _db.Tasks.Insert(u1);
        _vm.Refresh();
        _vm.ActiveListId = "list-custom";

        // Dragging a grouped task to the ungrouped drop slot lands at the end of the
        // ungrouped section (below the existing ungrouped task). Again, operate on the
        // instance the ViewModel holds (see the comment in the sibling test).
        _vm.MoveTaskToGroupCommand.Execute((_vm.Tasks.First(t => t.Id == "a1"), null));

        var moved = _vm.Tasks.First(t => t.Id == "a1");
        Assert.Null(moved.GroupId);
        Assert.Equal(1, moved.Order);   // after u1 (Order 0)
        Assert.Equal(new[] { "u1", "a1" }, _vm.GroupedTaskList[0].Tasks.Select(t => t.Id));
    }

    [Fact]
    public void RefreshActiveTasks_RecomputesSidebarCounts()
    {
        // Tasks: 2 open + 1 closed
        SeedTask("t1", "list-tasks");
        SeedTask("t2", "list-tasks");
        SeedTask("t3", "list-tasks", closed: true);
        // My Day: 1 open flagged (no due date, so it stays out of Planned)
        SeedTask("t4", "list-myday", myDay: true, myDayOrder: 0);
        // Important / Planned aggregate globally, so park them in a list with no own
        // sidebar entry — otherwise they'd inflate the list-tasks count below.
        _db.Tasks.Insert(new TaskItem { Id = "t5", ListId = "list-x", Title = "imp", IsImportant = true });
        SeedTask("t6", "list-x", due: Today.AddDays(7));
        // Custom: 1 open
        _db.Lists.Insert(new TaskList { Id = "list-custom", Name = "Custom", Type = ListType.Custom, Order = 1 });
        SeedTask("t7", "list-custom");

        _vm.Refresh();
        _vm.RefreshActiveTasks();

        Assert.Equal(2, CountFor("list-tasks"));      // t1 + t2 (closed t3 excluded)
        Assert.Equal(1, CountFor("list-myday"));      // t4
        Assert.Equal(1, CountFor("list-important"));  // t5
        Assert.Equal(1, CountFor("list-planned"));    // t6
        Assert.Equal(1, CountFor("list-custom"));     // t7
    }

    [Fact]
    public void DailyMyDayReset_RemovesYesterdayTasks_AndAutoAddsTodayTasks()
    {
        // A: yesterday's My Day task → reset out of My Day
        SeedTask("A", "list-tasks", myDay: true, myDayOrder: 5, due: Today.AddDays(-1));
        // B: due today, not yet in My Day → auto-added, order after the existing max
        SeedTask("B", "list-tasks", myDay: false, myDayOrder: -1, due: Today);
        // C: already in My Day for today → untouched
        SeedTask("C", "list-tasks", myDay: true, myDayOrder: 2, due: Today);

        _vm.Refresh();
        _vm.DailyMyDayReset();

        Assert.False(Task("A").IsMyDay);
        Assert.Equal(-1, Task("A").MyDayOrder);
        Assert.True(Task("B").IsMyDay);
        Assert.Equal(3, Task("B").MyDayOrder);   // max surviving My Day order (C=2) + 1
        Assert.True(Task("C").IsMyDay);
        Assert.Equal(2, Task("C").MyDayOrder);
    }

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
