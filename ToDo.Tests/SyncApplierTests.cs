using System;
using System.IO;
using System.Linq;
using ToDo.Models;
using ToDo.Services;
using ToDo.Sync;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises ApplySync: client-side LWW, per-device IsMyDay preservation, and the
/// tombstone cascades that prevent orphaned data after a remote delete.
/// </summary>
public sealed class SyncApplierTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;

    public SyncApplierTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static SyncChange Change(object entity) => SyncEntitySerializer.ToChange(entity)!;

    private static SyncChange Tombstone(string type, string id, long modifiedAt) =>
        new() { Type = type, Id = id, ModifiedAt = modifiedAt, Deleted = true };

    // Seeding local state via ApplySync (not the tracked collections) lets the tests
    // pin ModifiedAt exactly — ApplySync never re-stamps, per the LWW contract.

    [Fact]
    public void RemoteNewerTask_IsApplied()
    {
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "initial", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "from-server", ModifiedAt = 200 }) });

        var local = _db.Tasks.FindById("t1");
        Assert.Equal("from-server", local.Title);
        Assert.Equal(200, local.ModifiedAt);   // server value preserved, not re-stamped
    }

    [Fact]
    public void LocalNewerTask_IsSkipped()
    {
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "local", ModifiedAt = 200 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "stale", ModifiedAt = 100 }) });

        Assert.Equal("local", _db.Tasks.FindById("t1").Title);
    }

    [Fact]
    public void EqualModifiedAt_IsApplied()
    {
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "local", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "equal", ModifiedAt = 100 }) });

        Assert.Equal("equal", _db.Tasks.FindById("t1").Title);
    }

    [Fact]
    public void ApplyTask_PreservesLocalIsMyDay()
    {
        var local = new TaskItem { Id = "t1", ListId = "list-1", Title = "a", IsMyDay = true, MyDayOrder = 7 };
        _db.Tasks.Insert(local);  // stamps a (large) local ModifiedAt; IsMyDay persisted in the row

        var remote = new TaskItem { Id = "t1", ListId = "list-1", Title = "remote", IsImportant = true, ModifiedAt = 3_000_000_000_000 };
        _db.ApplySync(new[] { Change(remote) });

        var merged = _db.Tasks.FindById("t1");
        Assert.Equal("remote", merged.Title);
        Assert.True(merged.IsImportant);
        Assert.True(merged.IsMyDay);      // per-device flag survives
        Assert.Equal(7, merged.MyDayOrder);
    }

    [Fact]
    public void TaskTombstone_DeletesTask()
    {
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "a", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.Task, "t1", 200) });

        Assert.Null(_db.Tasks.FindById("t1"));
    }

    [Fact]
    public void TaskTombstone_LocalNewerEdit_KeepsTask()
    {
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "a", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "edited", ModifiedAt = 200 }) });
        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.Task, "t1", 150) });  // stale tombstone

        var local = _db.Tasks.FindById("t1");
        Assert.NotNull(local);
        Assert.Equal("edited", local.Title);
    }

    [Fact]
    public void ListTombstone_CascadesTasksToInbox_AndDeletesGroups()
    {
        _db.ApplySync(new[] { Change(new TaskList { Id = "list-1", Name = "Work", Type = ListType.Custom, ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskGroup { Id = "g1", ListId = "list-1", Name = "g", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", GroupId = "g1", Title = "a", ModifiedAt = 100 }) });

        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.List, "list-1", 200) });

        Assert.Null(_db.Lists.FindById("list-1"));
        Assert.Null(_db.Groups.FindById("g1"));
        var task = _db.Tasks.FindById("t1");
        Assert.Equal("list-tasks", task.ListId);  // orphaned task → inbox, like the app's DeleteList
        Assert.Null(task.GroupId);
    }

    [Fact]
    public void GroupTombstone_NullsTaskGroupId()
    {
        _db.ApplySync(new[] { Change(new TaskGroup { Id = "g1", ListId = "list-1", Name = "g", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", GroupId = "g1", Title = "a", ModifiedAt = 100 }) });

        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.Group, "g1", 200) });

        Assert.Null(_db.Groups.FindById("g1"));
        Assert.Null(_db.Tasks.FindById("t1").GroupId);
    }

    [Fact]
    public void ListGroupTombstone_NullsListGroupId()
    {
        _db.ApplySync(new[] { Change(new ListGroup { Id = "lg1", Name = "lg", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskList { Id = "list-1", Name = "Work", Type = ListType.Custom, GroupId = "lg1", ModifiedAt = 100 }) });

        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.ListGroup, "lg1", 200) });

        Assert.Null(_db.ListGroups.FindById("lg1"));
        Assert.Null(_db.Lists.FindById("list-1").GroupId);
    }

    [Fact]
    public void TagTombstone_StripsTagFromTasks()
    {
        _db.ApplySync(new[] { Change(new Tag { Id = "tag1", Name = "red", Color = "#f00", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "a", TagIds = new() { "tag1", "other" }, ModifiedAt = 100 }) });

        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.Tag, "tag1", 200) });

        Assert.Null(_db.Tags.FindById("tag1"));
        Assert.Equal(new[] { "other" }, _db.Tasks.FindById("t1").TagIds);
    }

    [Fact]
    public void EmptyPayload_IsIgnored()
    {
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "a", ModifiedAt = 100 }) });
        _db.ApplySync(new[] { new SyncChange { Type = SyncEntityTypes.Task, Id = "t1", ModifiedAt = 200, Payload = "" } });

        Assert.Equal("a", _db.Tasks.FindById("t1").Title);  // untouched
    }

    [Fact]
    public void LocalNewerConflict_IsReportedViaSyncDiagnostics()
    {
        var captured = new List<string>();
        SyncDiagnostics.Log = m => captured.Add(m);
        try
        {
            _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "local", ModifiedAt = 200 }) });
            _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "stale", ModifiedAt = 100 }) });
        }
        finally
        {
            SyncDiagnostics.Log = null;
        }

        Assert.Equal("local", _db.Tasks.FindById("t1").Title);   // local wins (LWW)
        Assert.Contains(captured, m => m.Contains("conflict") && m.Contains("t1"));
    }

    [Fact]
    public void ApplyFailure_IsReportedViaSyncDiagnostics()
    {
        var captured = new List<string>();
        SyncDiagnostics.LogWarn = m => captured.Add(m);
        try
        {
            // A Task change whose payload isn't valid JSON → deserialization throws.
            _db.ApplySync(new[] { new SyncChange { Type = SyncEntityTypes.Task, Id = "t1", ModifiedAt = 100, Payload = "not-json" } });
        }
        finally
        {
            SyncDiagnostics.LogWarn = null;
        }

        Assert.Contains(captured, m => m.Contains("failed") && m.Contains("t1"));
    }

    [Fact]
    public void ApplyListUpsert_NeverWipesLocalBackgroundBytes()
    {
        // Local list with an image background, then a newer server copy of the SAME list arrives.
        _db.SetListBackground("list-1", new byte[] { 1, 2, 3 }, "bg.png");
        _db.ApplySync(new[] { Change(new TaskList { Id = "list-1", Name = "Work", Type = ListType.Custom, ModifiedAt = 200 }) });

        // Background bytes live in a separate untracked collection, untouched by the upsert.
        Assert.Equal("Work", _db.Lists.FindById("list-1").Name);
        Assert.Equal(new byte[] { 1, 2, 3 }, _db.GetListBackgroundData("list-1"));
        Assert.Equal("bg.png", _db.GetListBackgroundFileName("list-1"));
    }

    [Fact]
    public void ListTombstone_DeletesLocalBackgroundBytes()
    {
        _db.ApplySync(new[] { Change(new TaskList { Id = "list-1", Name = "Work", Type = ListType.Custom, ModifiedAt = 100 }) });
        _db.SetListBackground("list-1", new byte[] { 1, 2, 3 }, "bg.png");

        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.List, "list-1", 200) });

        Assert.Null(_db.Lists.FindById("list-1"));
        Assert.Null(_db.GetListBackgroundData("list-1"));   // no orphaned bytes
        Assert.Null(_db.GetListBackgroundFileName("list-1"));
    }

    [Fact]
    public void ApplyListUpsert_NeverWipesLocalThemeSettings()
    {
        _db.SetListThemeSettings("list-1", 60, 50, 2);
        _db.ApplySync(new[] { Change(new TaskList { Id = "list-1", Name = "Work", Type = ListType.Custom, ModifiedAt = 200 }) });

        // The display settings (背景强弱 + 卡片不透明度 + 标题文字) live in their own
        // untracked collection, untouched by the whole-entity upsert.
        Assert.Equal("Work", _db.Lists.FindById("list-1").Name);
        var (background, card, title) = _db.GetListThemeSettings("list-1");
        Assert.Equal(60, background);
        Assert.Equal(50, card);
        Assert.Equal(2, title);
    }

    [Fact]
    public void ListTombstone_DeletesLocalThemeSetting()
    {
        _db.ApplySync(new[] { Change(new TaskList { Id = "list-1", Name = "Work", Type = ListType.Custom, ModifiedAt = 100 }) });
        _db.SetListThemeSettings("list-1", 60, 50, 2);

        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.List, "list-1", 200) });

        Assert.Null(_db.Lists.FindById("list-1"));
        var (background, card, title) = _db.GetListThemeSettings("list-1");
        Assert.Equal(100, background);   // no orphaned setting row
        Assert.Equal(65, card);
        Assert.Equal(0, title);
    }
}
