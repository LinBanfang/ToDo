using System;
using System.IO;
using System.Linq;
using ToDo.Models;
using ToDo.Services;
using ToDo.Sync;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Recurring-task engine (ADR-015): the pure next-due date math, close-time generation
/// (complete/skip spawns the next instance; stop-repeating clears the rule), the
/// at-most-one-open-instance guard, and the cross-device series dedup. DB-backed cases
/// seed via ApplySync so ModifiedAt can be pinned exactly (ApplySync never re-stamps).
/// </summary>
public sealed class RecurrenceServiceTests : IDisposable
{
    private static readonly DateTime Today = new(2026, 8, 11);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;

    public RecurrenceServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static long Ms(DateTime dt) => new DateTimeOffset(dt).ToUnixTimeMilliseconds();

    /// <summary>Task with a recurrence rule and a due date, for direct service calls.</summary>
    private static TaskItem Recurring(RecurrenceFrequency freq, DateTime due, string id = "t1") => new()
    {
        Id = id,
        Title = id,
        ListId = "l",
        Recurrence = freq,
        RecurrenceInterval = 1,
        DueDate = Ms(due),
    };

    /// <summary>Seed a task into the DB with an exact ModifiedAt (bypasses the tracked
    /// collections' re-stamp, per the LWW contract used by the other ApplySync tests).</summary>
    private void Seed(TaskItem t) => _db.ApplySync(new[] { SyncEntitySerializer.ToChange(t)! });

    private string[] PendingEntityIds() =>
        _db.Tracker.AllPending().Where(e => !e.Deleted).Select(e => e.EntityId).OrderBy(x => x).ToArray();

    private string[] PendingTombstones() =>
        _db.Tracker.AllPending().Where(e => e.Deleted).Select(e => e.EntityId).OrderBy(x => x).ToArray();

    // ─── ComputeNextDue: pure date math ──────────────────────

    [Fact]
    public void ComputeNextDue_None_ReturnsNull() =>
        Assert.Null(RecurrenceService.ComputeNextDue(RecurrenceFrequency.None, 1, new DateTime(2026, 8, 11), Today));

    [Fact]
    public void ComputeNextDue_IntervalZero_ReturnsNull() =>
        Assert.Null(RecurrenceService.ComputeNextDue(RecurrenceFrequency.Daily, 0, new DateTime(2026, 8, 11), Today));

    [Fact]
    public void ComputeNextDue_Daily_AdvancesOneDay()
    {
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Daily, 1, new DateTime(2026, 8, 11, 9, 0, 0), Today);
        Assert.Equal(new DateTime(2026, 8, 12, 9, 0, 0), next);
    }

    [Fact]
    public void ComputeNextDue_Daily_LateCompletion_LandsTomorrow()
    {
        // Due yesterday, finished today → next is tomorrow, not today.
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Daily, 1, new DateTime(2026, 8, 10, 9, 0, 0), Today);
        Assert.Equal(new DateTime(2026, 8, 12, 9, 0, 0), next);
    }

    [Theory]
    [InlineData("2026-08-10", "2026-08-11")]   // Mon → Tue
    [InlineData("2026-08-14", "2026-08-17")]   // Fri → Mon
    [InlineData("2026-08-15", "2026-08-17")]   // Sat → Mon
    [InlineData("2026-08-16", "2026-08-17")]   // Sun → Mon
    public void ComputeNextDue_Weekdays_SkipsWeekends(string dueStr, string expectedStr)
    {
        // today sits before every due above, so each case advances exactly one occurrence
        // (isolating the weekend skip — the catch-up loop is covered separately).
        var due = DateTime.Parse(dueStr);
        var expected = DateTime.Parse(expectedStr);
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Weekdays, 1, due, new DateTime(2026, 8, 7));
        Assert.Equal(expected, next);
    }

    [Fact]
    public void ComputeNextDue_Weekdays_KeepsTimeOfDay()
    {
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Weekdays, 1, new DateTime(2026, 8, 10, 9, 0, 0), new DateTime(2026, 8, 7));
        Assert.Equal(new DateTime(2026, 8, 11, 9, 0, 0), next);   // Mon 09:00 → Tue 09:00
    }

    [Fact]
    public void ComputeNextDue_Weekly_KeepsWeekday()
    {
        // Due Monday 08-10, finished Friday 08-14 → next Monday.
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Weekly, 1, new DateTime(2026, 8, 10), new DateTime(2026, 8, 14));
        Assert.Equal(new DateTime(2026, 8, 17), next);
    }

    [Fact]
    public void ComputeNextDue_Monthly_SameDayOfMonth()
    {
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Monthly, 1, new DateTime(2026, 8, 15), Today);
        Assert.Equal(new DateTime(2026, 9, 15), next);
    }

    [Fact]
    public void ComputeNextDue_Monthly_ClampsDayToMonthEnd()
    {
        // Jan 31 + 1 month → Feb 28 (2026 is a common year).
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Monthly, 1, new DateTime(2026, 1, 31), new DateTime(2026, 1, 15));
        Assert.Equal(new DateTime(2026, 2, 28), next);
    }

    [Fact]
    public void ComputeNextDue_Monthly_CatchesUpToNextFutureOccurrence()
    {
        // Last occurrence March, finished in August → the next future 15th.
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Monthly, 1, new DateTime(2026, 3, 15), new DateTime(2026, 8, 10));
        Assert.Equal(new DateTime(2026, 8, 15), next);
    }

    [Fact]
    public void ComputeNextDue_Yearly_ClampsLeapDayToFeb28()
    {
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Yearly, 1, new DateTime(2024, 2, 29), new DateTime(2024, 1, 1));
        Assert.Equal(new DateTime(2025, 2, 28), next);
    }

    [Fact]
    public void ComputeNextDue_Yearly_IntervalFour_KeepsLeapDay()
    {
        // 2028 (leap) + 4 years → 2032 (leap): Feb 29 survives.
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Yearly, 4, new DateTime(2028, 2, 29), new DateTime(2028, 1, 1));
        Assert.Equal(new DateTime(2032, 2, 29), next);
    }

    [Fact]
    public void ComputeNextDue_IntervalTwo_AdvancesTwoDays()
    {
        var next = RecurrenceService.ComputeNextDue(RecurrenceFrequency.Daily, 2, new DateTime(2026, 8, 11, 8, 0, 0), Today);
        Assert.Equal(new DateTime(2026, 8, 13, 8, 0, 0), next);
    }

    // ─── TryGenerateNext: close-time generation ─────────────

    [Fact]
    public void TryGenerateNext_NonRecurring_ReturnsNull_NothingInserted()
    {
        var t = new TaskItem { Id = "t1", Title = "once", ListId = "l" };
        var next = RecurrenceService.TryGenerateNext(_db, t, Today, endSeries: false);
        Assert.Null(next);
        Assert.Empty(_db.Tasks.FindAll());
    }

    [Fact]
    public void TryGenerateNext_Complete_GeneratesNextInstance()
    {
        var root = Recurring(RecurrenceFrequency.Daily, new DateTime(2026, 8, 11, 9, 0, 0), id: "root");
        Seed(root);

        var next = RecurrenceService.TryGenerateNext(_db, root, Today, endSeries: false);

        Assert.NotNull(next);
        Assert.Equal(Ms(new DateTime(2026, 8, 12, 9, 0, 0)), next.DueDate);
        Assert.Equal("root", next.RecurrenceSeriesId);          // points back to the series root
        Assert.Equal(RecurrenceFrequency.Daily, next.Recurrence);
        Assert.False(next.IsMyDay);                              // per-device state not copied
        Assert.Equal("root", next.Title);
        Assert.True(next.ModifiedAt > 0);                        // stamped by the tracked insert
        Assert.Equal(new[] { next.Id }, PendingEntityIds());     // and outboxed for sync
    }

    [Fact]
    public void TryGenerateNext_CopiesFields_ResetsSteps_ShiftsReminder()
    {
        var root = Recurring(RecurrenceFrequency.Weekly, new DateTime(2026, 8, 10, 9, 0, 0), id: "root");
        root.GroupId = "g1";
        root.IsImportant = true;
        root.TagIds = new() { "tag-a" };
        root.Steps.Add(new TaskStep { Title = "step1", Order = 0, Completed = true });
        root.Steps.Add(new TaskStep { Title = "step2", Order = 1 });
        root.Reminder = Ms(new DateTime(2026, 8, 10, 8, 30, 0));   // 30 min before
        Seed(root);

        var next = RecurrenceService.TryGenerateNext(_db, root, Today, endSeries: false);

        Assert.NotNull(next);
        Assert.Equal("g1", next.GroupId);
        Assert.True(next.IsImportant);
        Assert.Equal(new[] { "tag-a" }, next.TagIds);
        Assert.All(next.Steps, s => Assert.False(s.Completed));      // steps reset, per ADR-015
        Assert.Equal(2, next.Steps.Count);
        // Reminder keeps its 30-min offset relative to the new due date (next Monday 09:00).
        Assert.Equal(Ms(new DateTime(2026, 8, 17, 8, 30, 0)), next.Reminder);
    }

    [Fact]
    public void TryGenerateNext_EndSeries_ClearsRule_NoInsert()
    {
        var root = Recurring(RecurrenceFrequency.Weekly, new DateTime(2026, 8, 10), id: "root");
        Seed(root);

        var next = RecurrenceService.TryGenerateNext(_db, root, Today, endSeries: true);

        Assert.Null(next);
        Assert.Equal(RecurrenceFrequency.None, root.Recurrence);     // rule cleared on the instance
        Assert.Equal(1, root.RecurrenceInterval);
        Assert.Single(_db.Tasks.FindAll());                          // only the cleared root remains, no new insert
        Assert.Empty(PendingEntityIds());
    }

    [Fact]
    public void TryGenerateNext_Guard_RefusesWhenSiblingOpen()
    {
        var root = Recurring(RecurrenceFrequency.Weekly, new DateTime(2026, 8, 10), id: "root");
        root.ModifiedAt = 100;
        var sibling = new TaskItem
        {
            Id = "s2", Title = "s2", ListId = "l",
            Recurrence = RecurrenceFrequency.Weekly, DueDate = Ms(new DateTime(2026, 8, 17)),
            RecurrenceSeriesId = "root", ModifiedAt = 200,
        };
        Seed(root);
        Seed(sibling);

        var next = RecurrenceService.TryGenerateNext(_db, root, Today, endSeries: false);

        Assert.Null(next);   // a series already has an open instance → never generate a third
        Assert.Empty(PendingEntityIds());
    }

    [Fact]
    public void TryGenerateNext_NoDueDate_ReturnsNull()
    {
        var t = new TaskItem
        {
            Id = "t1", Title = "t1", ListId = "l",
            Recurrence = RecurrenceFrequency.Daily,
        };
        var next = RecurrenceService.TryGenerateNext(_db, t, Today, endSeries: false);
        Assert.Null(next);
    }

    // ─── DedupeSeries: converge to one open instance per series ──

    [Fact]
    public void DedupeSeries_KeepsNewestModifiedAt_DeletesOther_WithTombstone()
    {
        var root = Recurring(RecurrenceFrequency.Weekly, new DateTime(2026, 8, 10), id: "root");
        root.ModifiedAt = 100;
        var dup = new TaskItem
        {
            Id = "dup", Title = "dup", ListId = "l",
            Recurrence = RecurrenceFrequency.Weekly, DueDate = Ms(new DateTime(2026, 8, 17)),
            RecurrenceSeriesId = "root", ModifiedAt = 200,
        };
        Seed(root);
        Seed(dup);

        RecurrenceService.DedupeSeries(_db);

        Assert.Null(_db.Tasks.FindById("root"));                       // older instance removed
        Assert.NotNull(_db.Tasks.FindById("dup"));                     // newest ModifiedAt survives
        Assert.Equal(new[] { "root" }, PendingTombstones());           // tracked delete → tombstone syncs the dedup
    }

    [Fact]
    public void DedupeSeries_SingleOpenInstance_Untouched()
    {
        var root = Recurring(RecurrenceFrequency.Daily, new DateTime(2026, 8, 11), id: "root");
        root.ModifiedAt = 100;
        Seed(root);

        RecurrenceService.DedupeSeries(_db);

        Assert.NotNull(_db.Tasks.FindById("root"));
        Assert.Empty(PendingTombstones());
    }

    [Fact]
    public void DedupeSeries_DifferentSeries_BothKept()
    {
        var a = Recurring(RecurrenceFrequency.Weekly, new DateTime(2026, 8, 10), id: "a");
        var b = Recurring(RecurrenceFrequency.Weekly, new DateTime(2026, 8, 11), id: "b");
        Seed(a);
        Seed(b);

        RecurrenceService.DedupeSeries(_db);

        Assert.NotNull(_db.Tasks.FindById("a"));
        Assert.NotNull(_db.Tasks.FindById("b"));
        Assert.Empty(PendingTombstones());
    }

    // ─── Sync round-trip: the recurrence fields must survive the wire ──

    [Fact]
    public void SyncRoundTrip_PreservesRecurrenceFields()
    {
        var t = Recurring(RecurrenceFrequency.Monthly, new DateTime(2026, 8, 15, 9, 0, 0), id: "t1");
        t.RecurrenceSeriesId = "series";

        var back = (TaskItem)SyncEntitySerializer.FromChange(SyncEntitySerializer.ToChange(t)!)!;

        Assert.Equal(RecurrenceFrequency.Monthly, back.Recurrence);
        Assert.Equal(1, back.RecurrenceInterval);
        Assert.Equal("series", back.RecurrenceSeriesId);
    }

    [Fact]
    public void SyncRoundTrip_NonRecurringDefaultsToNone()
    {
        var t = new TaskItem { Id = "t1", Title = "t1", ListId = "l" };
        var back = (TaskItem)SyncEntitySerializer.FromChange(SyncEntitySerializer.ToChange(t)!)!;
        Assert.Equal(RecurrenceFrequency.None, back.Recurrence);
    }
}
