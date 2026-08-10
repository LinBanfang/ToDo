using System;
using System.IO;
using System.Linq;
using System.Threading;
using ToDo.Models;
using ToDo.Services;
using ToDo.Sync;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises the outbox: every tracked write stamps ModifiedAt and records a SyncEvent
/// (upserted per entity, tombstone on delete), system lists are excluded, and clearing
/// only removes what was actually pushed.
/// </summary>
public sealed class OutboxTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;

    public OutboxTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void InsertTask_RecordsOutboxEvent_WithRoundTrippablePayload()
    {
        _db.Tasks.Insert(new TaskItem { Id = "t1", Title = "hello", ListId = "list-1", Order = 0 });

        var ev = _db.Tracker.AllPending().Single(e => e.EntityId == "t1");
        Assert.Equal(SyncEntityTypes.Task, ev.EntityType);
        Assert.False(ev.Deleted);
        Assert.False(string.IsNullOrEmpty(ev.PayloadJson));

        var restored = (TaskItem)SyncEntitySerializer.FromChange(new SyncChange
        {
            Type = ev.EntityType, Id = ev.EntityId, ModifiedAt = ev.ModifiedAt, Payload = ev.PayloadJson,
        })!;
        Assert.Equal("hello", restored.Title);
        Assert.Equal("list-1", restored.ListId);
    }

    [Fact]
    public void UpdateTask_UpsertsSingleOutboxEntry()
    {
        var task = new TaskItem { Id = "t1", Title = "first", ListId = "l" };
        _db.Tasks.Insert(task);
        Thread.Sleep(5);
        task.Title = "second";
        _db.Tasks.Update(task);

        var events = _db.Tracker.AllPending().Where(e => e.EntityId == "t1").ToArray();
        Assert.Single(events);   // upsert, not append

        var restored = (TaskItem)SyncEntitySerializer.FromChange(new SyncChange
        {
            Type = events[0].EntityType, Id = events[0].EntityId, ModifiedAt = events[0].ModifiedAt, Payload = events[0].PayloadJson,
        })!;
        Assert.Equal("second", restored.Title);
    }

    [Fact]
    public void DeleteTask_RecordsTombstone()
    {
        _db.Tasks.Insert(new TaskItem { Id = "t1", Title = "x", ListId = "l" });
        _db.Tracker.Clear();
        _db.Tasks.Delete("t1");

        var ev = _db.Tracker.AllPending().Single(e => e.EntityId == "t1");
        Assert.True(ev.Deleted);
        Assert.True(ev.ModifiedAt > 0);
    }

    [Fact]
    public void DeleteMissingTask_RecordsNothing()
    {
        _db.Tracker.Clear();
        _db.Tasks.Delete("nope");
        Assert.Empty(_db.Tracker.AllPending());
    }

    [Fact]
    public void DeleteMany_RecordsTombstonePerEntity()
    {
        _db.Lists.Insert(new TaskList { Id = "lst", Name = "L", Type = ListType.Custom });
        _db.Groups.Insert(new TaskGroup { Id = "g1", ListId = "lst", Name = "g" });
        _db.Groups.Insert(new TaskGroup { Id = "g2", ListId = "lst", Name = "g" });
        _db.Tracker.Clear();

        _db.Groups.DeleteMany(g => g.ListId == "lst");

        var tombstones = _db.Tracker.AllPending().Where(e => e.Deleted).Select(e => e.EntityId).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "g1", "g2" }, tombstones);
    }

    [Fact]
    public void InsertBulk_RecordsEachEntity()
    {
        _db.Tracker.Clear();
        _db.Tasks.InsertBulk(new[]
        {
            new TaskItem { Id = "t1", ListId = "l" },
            new TaskItem { Id = "t2", ListId = "l" },
        });
        Assert.Equal(2, _db.Tracker.AllPending().Count());
    }

    [Fact]
    public void SystemListInsert_IsNotRecorded()
    {
        _db.Tracker.Clear();
        _db.Lists.Insert(new TaskList { Id = "list-fake", Name = "Sys", IsSystem = true, Type = ListType.Tasks });
        Assert.Empty(_db.Tracker.AllPending());
    }

    [Fact]
    public void TaskPayload_ExcludesMyDayFields()
    {
        var task = new TaskItem { Id = "t1", ListId = "l", Title = "x", IsMyDay = true, MyDayOrder = 3 };
        _db.Tasks.Insert(task);

        var json = _db.Tracker.AllPending().Single(e => e.EntityId == "t1").PayloadJson;
        Assert.DoesNotContain("IsMyDay", json);
        Assert.DoesNotContain("MyDayOrder", json);
    }

    [Fact]
    public void ListPayload_ExcludesTaskCount()
    {
        var list = new TaskList { Id = "lst", Name = "Work", Type = ListType.Custom, TaskCount = 99 };
        _db.Lists.Insert(list);

        var json = _db.Tracker.AllPending().Single(e => e.EntityId == "lst").PayloadJson;
        Assert.DoesNotContain("TaskCount", json);
    }

    [Fact]
    public void TrackingDisabled_RecordsNothing()
    {
        _db.Tracker.TrackingEnabled = false;
        try
        {
            _db.Tasks.Insert(new TaskItem { Id = "t1", Title = "x", ListId = "l" });
            _db.Tasks.Delete("t1");
            Assert.Empty(_db.Tracker.AllPending());
        }
        finally
        {
            _db.Tracker.TrackingEnabled = true;
        }
    }

    [Fact]
    public void ClearPushed_KeepsEventRewrittenDuringRoundTrip()
    {
        var task = new TaskItem { Id = "t1", Title = "before", ListId = "l" };
        _db.Tasks.Insert(task);                                   // M0
        var pushed = _db.Tracker.AllPending().ToList();

        Thread.Sleep(5);                                          // M1 strictly newer
        task.Title = "after";
        _db.Tasks.Update(task);                                   // rewrites the outbox entry

        _db.Tracker.ClearPushed(pushed);

        var remaining = _db.Tracker.AllPending().ToList();
        Assert.Single(remaining);                                 // the rewrite survives the flush
        Assert.True(remaining[0].ModifiedAt > pushed[0].ModifiedAt);
        Assert.Contains("after", remaining[0].PayloadJson);
    }

    [Fact]
    public void BootstrapSync_SeedsOutboxFromExistingData_ExcludingSystemLists()
    {
        _db.Lists.Insert(new TaskList { Id = "lst", Name = "Work", Type = ListType.Custom });
        _db.Tasks.Insert(new TaskItem { Id = "t1", Title = "x", ListId = "lst" });
        _db.Tracker.Clear();

        _db.BootstrapSync();

        var events = _db.Tracker.AllPending().ToList();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.EntityId == "lst" && !e.Deleted);
        Assert.Contains(events, e => e.EntityId == "t1" && !e.Deleted);
        Assert.DoesNotContain(events, e => e.EntityId == "list-myday"); // system lists never sync
    }

    [Fact]
    public void ListPayload_IncludesBackgroundFields()
    {
        var list = new TaskList
        {
            Id = "lst", Name = "Work", Type = ListType.Custom,
            BackgroundType = ListBackgroundType.Solid, BackgroundColor = "#28A745",
        };
        _db.Lists.Insert(list);

        var json = _db.Tracker.AllPending().Single(e => e.EntityId == "lst").PayloadJson;
        Assert.Contains("\"BackgroundType\":1", json);              // Solid == 1
        Assert.Contains("\"BackgroundColor\":\"#28A745\"", json);

        var restored = (TaskList)SyncEntitySerializer.FromChange(new SyncChange
        {
            Type = SyncEntityTypes.List, Id = "lst", ModifiedAt = 0, Payload = json,
        })!;
        Assert.Equal(ListBackgroundType.Solid, restored.BackgroundType);
        Assert.Equal("#28A745", restored.BackgroundColor);
    }

    [Fact]
    public void ListPayload_BackgroundFields_RoundTrip_EmptyColorIsNull()
    {
        var list = new TaskList
        {
            Id = "lst", Name = "Work", Type = ListType.Custom,
            BackgroundType = ListBackgroundType.Image, BackgroundColor = "",   // empty → null in payload
        };
        _db.Lists.Insert(list);

        var json = _db.Tracker.AllPending().Single(e => e.EntityId == "lst").PayloadJson;
        Assert.Contains("\"BackgroundType\":2", json);              // Image == 2
        Assert.DoesNotContain("BackgroundColor", json);             // empty color is omitted, not "null"

        var restored = (TaskList)SyncEntitySerializer.FromChange(new SyncChange
        {
            Type = SyncEntityTypes.List, Id = "lst", ModifiedAt = 0, Payload = json,
        })!;
        Assert.Equal(ListBackgroundType.Image, restored.BackgroundType);
        Assert.Equal("", restored.BackgroundColor);                 // absent field reads back as ""
    }
}
