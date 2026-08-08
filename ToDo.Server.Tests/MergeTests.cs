using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ToDo.Server;
using ToDo.Sync;
using Xunit;

namespace ToDo.Server.Tests;

/// <summary>
/// Exercises SyncStore.Merge directly (no HTTP) against a real temp SQLite file:
/// LWW accept/reject, seq monotonicity, tombstones, and the incremental cursor.
/// </summary>
public sealed class MergeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), "todo-sync-test-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly SyncDbContext _db;
    private readonly SyncStore _store;

    public MergeTests()
    {
        var options = new DbContextOptionsBuilder<SyncDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new SyncDbContext(options);
        _db.Database.EnsureCreated();
        _store = new SyncStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();   // pooled connections otherwise keep the file locked
        File.Delete(_dbPath);
    }

    private static SyncChange Change(string type, string id, long modifiedAt, string? payload = null, bool deleted = false) =>
        new() { Type = type, Id = id, ModifiedAt = modifiedAt, Deleted = deleted, Payload = payload };

    [Fact]
    public void FirstMerge_AssignsSeqFromOne()
    {
        var r = _store.Merge(new[] { Change("task", "t1", 100, "{...}") }, since: 0);

        Assert.Equal(1, r.ServerSeq);
        Assert.Equal("t1", Assert.Single(r.Changes).Id);
    }

    [Fact]
    public void NewerPush_OverwritesOlder()
    {
        _store.Merge(new[] { Change("task", "t1", 100, "old") }, since: 0);
        var r = _store.Merge(new[] { Change("task", "t1", 200, "new") }, since: 0);

        var c = Assert.Single(r.Changes);
        Assert.Equal("new", c.Payload);
        Assert.Equal(200, c.ModifiedAt);
    }

    [Fact]
    public void StalePush_IsRejected_AndNewerRowReturned()
    {
        _store.Merge(new[] { Change("task", "t1", 200, "newer") }, since: 0);
        var r = _store.Merge(new[] { Change("task", "t1", 150, "stale") }, since: 0);

        Assert.Equal(200, Assert.Single(r.Changes).ModifiedAt);   // stale push didn't overwrite
        Assert.Equal("newer", Assert.Single(r.Changes).Payload);
    }

    [Fact]
    public void EqualModifiedAt_LastPushWins()
    {
        _store.Merge(new[] { Change("task", "t1", 100, "first") }, since: 0);
        var r = _store.Merge(new[] { Change("task", "t1", 100, "second") }, since: 0);

        Assert.Equal("second", Assert.Single(r.Changes).Payload);
    }

    [Fact]
    public void Tombstone_IsStored_AndServed()
    {
        _store.Merge(new[] { Change("task", "t1", 100, "{...}") }, since: 0);
        var r = _store.Merge(new[] { Change("task", "t1", 200, deleted: true) }, since: 0);

        var c = Assert.Single(r.Changes);
        Assert.True(c.Deleted);
        Assert.Null(c.Payload);
    }

    [Fact]
    public void NewerNonDeleted_OverridesTombstone()
    {
        _store.Merge(new[] { Change("task", "t1", 200, deleted: true) }, since: 0);
        var r = _store.Merge(new[] { Change("task", "t1", 300, "reborn") }, since: 0);

        var c = Assert.Single(r.Changes);
        Assert.False(c.Deleted);
        Assert.Equal("reborn", c.Payload);
    }

    [Fact]
    public void IncrementalPull_ReturnsOnlyChangesAfterCursor()
    {
        _store.Merge(new[] { Change("task", "t1", 100, "a") }, since: 0);
        var r1 = _store.Merge(new[] { Change("task", "t2", 100, "b") }, since: 0);
        Assert.Equal(2, r1.ServerSeq);
        Assert.Equal(2, r1.Changes.Count);   // t1 (seq 1) and t2 (seq 2)

        var r2 = _store.Merge(Array.Empty<SyncChange>(), since: r1.ServerSeq);
        Assert.Empty(r2.Changes);
        Assert.Equal(2, r2.ServerSeq);
    }

    [Fact]
    public void PullAfterStaleRejection_ReturnsNewerVersion()
    {
        // device A syncs at seq 1 (t1@100), device B at seq 2 (t1@200)
        var r1 = _store.Merge(new[] { Change("task", "t1", 100, "a") }, since: 0);
        _store.Merge(new[] { Change("task", "t1", 200, "b") }, since: 1);

        // A, still at since=1, pushes its stale edit t1@150
        var r3 = _store.Merge(new[] { Change("task", "t1", 150, "stale") }, since: 1);

        Assert.Equal(2, r3.ServerSeq);            // rejected push hands out no new seq
        Assert.Equal("b", Assert.Single(r3.Changes).Payload);   // A receives the authoritative copy
        Assert.Equal(1, r1.ServerSeq);
    }

    [Fact]
    public void MultipleEntities_InOneBatch_GetModifiedAtOrderedSeqs()
    {
        var r = _store.Merge(new[]
        {
            Change("task", "t1", 100, "a"),
            Change("list", "l1", 50, "b"),
            Change("task", "t2", 200, "c"),
        }, since: 0);

        Assert.Equal(3, r.ServerSeq);
        Assert.Equal(new[] { "list:l1", "task:t1", "task:t2" }, r.Changes.Select(c => $"{c.Type}:{c.Id}"));
    }
}
