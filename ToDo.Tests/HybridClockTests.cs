using System;
using System.IO;
using System.Linq;
using ToDo.Models;
using ToDo.Services;
using ToDo.Sync;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises the HLC (ADR-018): encoding round-trips, monotonic ticks, causal merge via
/// Observe, persisted high-water-mark restore, and the one-time raw→HLC migration on the
/// database. The clock must guarantee "later edit always wins" regardless of wall-clock.
/// </summary>
public sealed class HybridClockTests
{
    [Fact]
    public void Tick_IsStrictlyMonotonic()
    {
        var clock = new HybridClock(1);
        var last = clock.Tick();
        for (var i = 0; i < 1000; i++)
        {
            var next = clock.Tick();
            Assert.True(next > last, $"tick regressed: {next} <= {last}");
            last = next;
        }
    }

    [Fact]
    public void Encode_DecodePhysical_RoundTrips()
    {
        var encoded = HybridClock.Encode(1_700_000_000_000, 123, 200);
        Assert.Equal(1_700_000_000_000, HybridClock.DecodePhysical(encoded));
    }

    [Fact]
    public void Encode_OrdersByPhysicalThenLogicalThenDiscriminator()
    {
        // Same physical + logical → higher discriminator wins; higher logical beats higher discriminator.
        var base_ = HybridClock.Encode(1000, 5, 1);
        Assert.True(HybridClock.Encode(1000, 5, 2) > base_);       // discriminator tiebreak
        Assert.True(HybridClock.Encode(1000, 6, 0) > base_);       // logical outranks discriminator
        Assert.True(HybridClock.Encode(1001, 0, 0) > base_);       // physical outranks everything
    }

    [Fact]
    public void DiscriminatorFor_Guid_IsStable_AndInRange_AndFallsBackToZero()
    {
        var id = Guid.NewGuid().ToString("N");
        var d = HybridClock.DiscriminatorFor(id);
        Assert.Equal(d, HybridClock.DiscriminatorFor(id));   // stable for the same device
        Assert.InRange(d, (byte)0, (byte)255);
        Assert.Equal((byte)0, HybridClock.DiscriminatorFor("not-a-guid"));
    }

    [Fact]
    public void Observe_ThenTick_IsCausallyAfterObservedValue()
    {
        var a = new HybridClock(1);
        var b = new HybridClock(2);

        var aTick = a.Tick();
        b.Observe(aTick);
        var bTick = b.Tick();

        Assert.True(bTick > aTick, "a device that merged a remote write must tick after it");
    }

    [Fact]
    public void Restore_KeepsFutureHighWaterMark_SoNtpRollbackCannotRegress()
    {
        var future = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000;
        var clock = new HybridClock(1, future, 5);

        Assert.Equal(future, clock.Physical);
        Assert.Equal(5, clock.Logical);

        // Wall clock is "now" (< future), so the next tick must still sort after the persisted mark.
        var tick = clock.Tick();
        Assert.True(tick >= HybridClock.Encode(future, 5, 1));
    }
}

/// <summary>Integration tests for the raw→HLC migration and HLC-stamped writes.</summary>
public sealed class HlcMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-tests-" + Guid.NewGuid().ToString("N"));
    private const long HlcEpoch = 1L << 50;

    public HlcMigrationTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void MigrateToHlc_RebasesRawRows_AndReseedsOutbox()
    {
        var dbPath = Path.Combine(_dir, "todo.db");

        // v1 (no clock): writes are stamped with raw wall-clock ms.
        using (var legacy = new DatabaseService(dbPath))
        {
            legacy.Tasks.Insert(new TaskItem { Id = "t1", ListId = "list-tasks", Title = "old" });
        }

        // Reopen as v2 with a clock, then migrate.
        using (var db = new DatabaseService(dbPath, new HybridClock(0)))
        {
            var before = db.Tasks.FindById("t1")!.ModifiedAt;
            Assert.True(before > 0 && before < HlcEpoch, "pre-migration row should be raw wall-clock ms");

            db.MigrateToHlc();

            Assert.Equal(before << 21, db.Tasks.FindById("t1")!.ModifiedAt);
            Assert.Contains(db.Tracker.AllPending(),
                e => e.EntityId == "t1" && !e.Deleted && e.ModifiedAt == before << 21);
        }
    }

    [Fact]
    public void MigrateToHlc_IsIdempotent()
    {
        var dbPath = Path.Combine(_dir, "todo.db");
        using (var legacy = new DatabaseService(dbPath))
        {
            legacy.Tasks.Insert(new TaskItem { Id = "t1", ListId = "list-tasks", Title = "old" });
        }

        using (var db = new DatabaseService(dbPath, new HybridClock(0)))
        {
            db.MigrateToHlc();
            var afterFirst = db.Tasks.FindById("t1")!.ModifiedAt;
            Assert.True(afterFirst >= HlcEpoch);

            db.MigrateToHlc();   // second run must not double-shift

            Assert.Equal(afterFirst, db.Tasks.FindById("t1")!.ModifiedAt);
        }
    }

    [Fact]
    public void TrackedInsert_WithClock_StampsHlcNotRaw()
    {
        var dbPath = Path.Combine(_dir, "todo.db");
        using var db = new DatabaseService(dbPath, new HybridClock(0));

        var task = new TaskItem { Id = "t1", ListId = "list-tasks", Title = "new" };
        db.Tasks.Insert(task);

        Assert.True(task.ModifiedAt >= HlcEpoch, "HLC-stamped writes must encode above the raw-ms epoch");
    }

    [Fact]
    public void ApplySync_WithClock_ObservesRemote_ThenNextWriteWins()
    {
        var dbPath = Path.Combine(_dir, "todo.db");
        using var db = new DatabaseService(dbPath, new HybridClock(0));

        // A remote change with a large HLC timestamp.
        var remote = SyncEntitySerializer.ToChange(
            new TaskItem { Id = "t1", ListId = "list-tasks", Title = "remote", ModifiedAt = HybridClock.Encode(2_000_000_000_000, 0, 7) })!;
        db.ApplySync(new[] { remote });

        // A local write afterwards must sort after the observed remote timestamp.
        var local = new TaskItem { Id = "t2", ListId = "list-tasks", Title = "local" };
        db.Tasks.Insert(local);

        Assert.True(local.ModifiedAt > remote.ModifiedAt,
            "post-sync local write must be causally after the merged remote change");
    }
}
