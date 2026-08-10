using System;
using System.IO;
using System.Linq;
using LiteDB;
using ToDo.Models;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises the TrackedCollection overloads the app's hot paths don't reach — the
/// BsonValue-id / IEnumerable / Upsert / DeleteAll / BsonExpression variants — pinning
/// that every one stamps ModifiedAt and records (or tombstones) the outbox exactly once.
/// OutboxTests covers the everyday Insert(T)/Update(T)/Delete(string) paths.
/// </summary>
public sealed class TrackedCollectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;

    public TrackedCollectionTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static TaskItem MakeTask(string id) => new() { Id = id, Title = id, ListId = "l" };

    private string[] PendingEntityIds() =>
        _db.Tracker.AllPending().Where(e => !e.Deleted).Select(e => e.EntityId).OrderBy(x => x).ToArray();

    private string[] PendingTombstones() =>
        _db.Tracker.AllPending().Where(e => e.Deleted).Select(e => e.EntityId).OrderBy(x => x).ToArray();

    [Fact]
    public void Upsert_NewId_StampsAndRecordsOnce()
    {
        _db.Tracker.Clear();
        var t = MakeTask("t1");

        _db.Tasks.Upsert(t);

        Assert.True(t.ModifiedAt > 0);
        var events = _db.Tracker.AllPending().Where(e => e.EntityId == "t1").ToArray();
        Assert.Single(events);
        Assert.False(events[0].Deleted);
    }

    [Fact]
    public void Upsert_ExistingId_KeepsSingleOutboxEntry()
    {
        _db.Tasks.Upsert(MakeTask("t1"));
        var updated = MakeTask("t1");
        updated.Title = "v2";

        _db.Tasks.Upsert(updated);

        Assert.Single(_db.Tracker.AllPending(), e => e.EntityId == "t1");
    }

    [Fact]
    public void Upsert_BsonValueId_Records()
    {
        _db.Tracker.Clear();
        _db.Tasks.Upsert(new BsonValue("t1"), MakeTask("t1"));
        Assert.Equal(new[] { "t1" }, PendingEntityIds());
    }

    [Fact]
    public void Insert_BsonValueId_RecordsAndStamps()
    {
        _db.Tracker.Clear();
        var t = MakeTask("t1");
        _db.Tasks.Insert(new BsonValue("t1"), t);
        Assert.True(t.ModifiedAt > 0);
        Assert.Equal(new[] { "t1" }, PendingEntityIds());
    }

    [Fact]
    public void Update_BsonValueId_Records()
    {
        _db.Tasks.Insert(MakeTask("t1"));
        _db.Tracker.Clear();
        _db.Tasks.Update(new BsonValue("t1"), MakeTask("t1"));
        Assert.Equal(new[] { "t1" }, PendingEntityIds());
    }

    [Fact]
    public void Insert_Range_RecordsEach_AndStamps()
    {
        _db.Tracker.Clear();
        var items = new[] { MakeTask("t1"), MakeTask("t2") };

        _db.Tasks.Insert((IEnumerable<TaskItem>)items);

        Assert.All(items, t => Assert.True(t.ModifiedAt > 0));
        Assert.Equal(new[] { "t1", "t2" }, PendingEntityIds());
    }

    [Fact]
    public void Update_Range_RecordsEach()
    {
        _db.Tasks.Insert(MakeTask("t1"));
        _db.Tasks.Insert(MakeTask("t2"));
        _db.Tracker.Clear();

        _db.Tasks.Update(new[] { MakeTask("t1"), MakeTask("t2") });

        Assert.Equal(new[] { "t1", "t2" }, PendingEntityIds());
    }

    [Fact]
    public void Upsert_Range_RecordsEach()
    {
        _db.Tracker.Clear();
        _db.Tasks.Upsert(new[] { MakeTask("t1"), MakeTask("t2") });
        Assert.Equal(new[] { "t1", "t2" }, PendingEntityIds());
    }

    [Fact]
    public void Delete_BsonValue_RecordsTombstone_WhenExisted()
    {
        _db.Tasks.Insert(MakeTask("t1"));
        _db.Tracker.Clear();

        _db.Tasks.Delete(new BsonValue("t1"));

        Assert.Equal(new[] { "t1" }, PendingTombstones());
    }

    [Fact]
    public void DeleteAll_RecordsTombstonePerEntity_AndEmpties()
    {
        _db.Tasks.Insert(MakeTask("t1"));
        _db.Tasks.Insert(MakeTask("t2"));
        _db.Tracker.Clear();

        _db.Tasks.DeleteAll();

        Assert.Empty(_db.Tasks.FindAll());
        Assert.Equal(new[] { "t1", "t2" }, PendingTombstones());
    }

    [Fact]
    public void Update_MissingEntity_ReturnsFalse_RecordsNothing()
    {
        _db.Tracker.Clear();
        var ok = _db.Tasks.Update(MakeTask("missing"));
        Assert.False(ok);
        Assert.Empty(_db.Tracker.AllPending());
    }

    [Fact]
    public void DeleteMany_BsonExpression_RecordsTombstones()
    {
        _db.Tasks.Insert(MakeTask("t1"));
        _db.Tasks.Insert(MakeTask("t2"));
        _db.Tracker.Clear();

        var n = _db.Tasks.DeleteMany(BsonExpression.Create("$.ListId = 'l'"));

        Assert.Equal(2, n);
        Assert.Equal(new[] { "t1", "t2" }, PendingTombstones());
    }
}
