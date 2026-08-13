using System;
using ToDo.Models;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Pure reconcile logic over a fake toast store: a native toast is scheduled for every
/// open task's future reminder at <c>reminder + grace</c> (default 45s — larger than the
/// 15s poll so the in-app card always wins when the app is running), stale tags are
/// removed, and a reminder that already fired in-app is suppressed within the grace
/// window so the OS doesn't double-notify. The WinRT store itself is a thin glue layer
/// and is not unit-tested.
/// </summary>
public sealed class NativeReminderSchedulerTests
{
    private const long Now = 1_700_000_000_000;   // fixed unix ms
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(45);
    private const long GraceMs = 45_000;

    private readonly FakeToastStore _store = new();

    private static TaskItem Open(string id, long reminder) =>
        new() { Id = id, ListId = "list-tasks", Title = $"Task {id}", Reminder = reminder };

    private static TaskItem Closed(string id, long reminder) =>
        new() { Id = id, ListId = "list-tasks", Title = $"Task {id}", Reminder = reminder,
                CloseRecord = new CloseRecord { ClosedAt = Now } };

    private static TaskItem OpenFired(string id, long reminder) =>
        new() { Id = id, ListId = "list-tasks", Title = $"Task {id}", Reminder = reminder,
                FiredReminder = reminder };

    private NativeReminderScheduler NewScheduler() => new(_store, Grace);

    [Fact]
    public void Reconcile_SchedulesFutureOpenReminder_AtReminderPlusGrace()
    {
        var svc = NewScheduler();
        var rem = Now + 60_000;
        var t = Open("a", rem);

        svc.Reconcile(new[] { t }, Now);

        var entry = Assert.Single(_store.Scheduled);
        Assert.Equal($"a|{rem}", entry.Key);
        Assert.Equal("Task a", entry.Value.Title);
        Assert.Equal(rem + GraceMs, entry.Value.FireAt);
    }

    [Fact]
    public void Reconcile_IgnoresClosedAndPastAndMissingReminders()
    {
        var svc = NewScheduler();
        var pastRem = Now - 60_000;                          // due long ago → in-app catch-up owns it
        var tasks = new[]
        {
            Closed("closed", Now + 60_000),                  // completed → no toast
            Open("past", pastRem),                           // fireAt already in the past → skip
            new TaskItem { Id = "none", ListId = "list-tasks", Title = "No reminder" },
        };

        svc.Reconcile(tasks, Now);

        Assert.Empty(_store.Scheduled);
    }

    [Fact]
    public void Reconcile_RemovesStaleTag_WhenTaskRescheduled()
    {
        var svc = NewScheduler();
        var oldRem = Now + 60_000;
        var t = Open("a", oldRem);
        svc.Reconcile(new[] { t }, Now);
        Assert.Contains($"a|{oldRem}", _store.Scheduled);

        var newRem = Now + 120_000;
        t.Reminder = newRem;
        svc.Reconcile(new[] { t }, Now);

        Assert.Single(_store.Scheduled);                     // old removed, new added
        Assert.Contains($"a|{newRem}", _store.Scheduled);
        Assert.DoesNotContain($"a|{oldRem}", _store.Scheduled);
    }

    [Fact]
    public void Reconcile_RemovesStaleTag_WhenTaskClosedOrDropped()
    {
        var svc = NewScheduler();
        var rem = Now + 60_000;
        var t = Open("a", rem);
        svc.Reconcile(new[] { t }, Now);
        Assert.Single(_store.Scheduled);

        svc.Reconcile(new[] { Closed("a", rem) }, Now);      // closed → not desired anymore
        Assert.Empty(_store.Scheduled);
    }

    [Fact]
    public void Reconcile_SuppressesAlreadyFiredReminder_FromAnotherDevice()
    {
        var svc = NewScheduler();
        var rem = Now + 60_000;
        // FiredReminder == Reminder → the reminder already fired (possibly on another
        // device, ADR-019); no native toast is scheduled even though it's open + future.
        var t = OpenFired("a", rem);

        svc.Reconcile(new[] { t }, Now);

        Assert.Empty(_store.Scheduled);
    }

    [Fact]
    public void Reconcile_SecondIdenticalCall_DoesNotReschedule()
    {
        var svc = NewScheduler();
        var t = Open("a", Now + 60_000);
        svc.Reconcile(new[] { t }, Now);
        var upsertCount = _store.UpsertCalls;

        svc.Reconcile(new[] { t }, Now);

        Assert.Equal(upsertCount, _store.UpsertCalls);       // mirror diff → no redundant AddToSchedule
    }

    [Fact]
    public void RemoveFired_DropsToast_AndSuppressesReaddWithinGrace()
    {
        var svc = NewScheduler();
        var rem = Now + 60_000;
        var t = Open("a", rem);
        svc.Reconcile(new[] { t }, Now);
        Assert.Single(_store.Scheduled);

        // In-app card fired → the pending native toast must not fire in the grace window.
        svc.RemoveFired("a", rem);
        Assert.Empty(_store.Scheduled);

        svc.Reconcile(new[] { t }, Now);                     // still future + open, but suppressed
        Assert.Empty(_store.Scheduled);

        // After the grace window has passed the fire time, the reminder is past anyway.
        svc.Reconcile(new[] { t }, Now + GraceMs + 1);
        Assert.Empty(_store.Scheduled);
    }

    [Fact]
    public void RemoveFired_UnknownKey_SkipsStoreRemove()
    {
        var svc = NewScheduler();
        svc.RemoveFired("ghost", Now + 1_000);
        Assert.Empty(_store.RemovedTags);                    // nothing was scheduled → nothing removed
    }

    [Fact]
    public void Ctor_ClearsStoreResidue()
    {
        var store = new FakeToastStore { Scheduled = { ["stale"] = ("t", "m", Now) } };
        var svc = new NativeReminderScheduler(store, Grace);
        Assert.Equal(1, store.ClearAllCalls);
        Assert.Empty(store.Scheduled);
    }

    [Fact]
    public void ClearAll_ClearsStoreMirrorAndSuppression()
    {
        var svc = NewScheduler();
        var rem = Now + 60_000;
        var t = Open("a", rem);
        svc.Reconcile(new[] { t }, Now);
        Assert.Single(_store.Scheduled);

        svc.RemoveFired("a", rem);                           // suppression recorded, toast dropped
        Assert.Empty(_store.Scheduled);

        svc.ClearAll();
        Assert.True(_store.ClearAllCalls >= 1);

        // Suppression was cleared too → a fresh reconcile schedules again.
        svc.Reconcile(new[] { t }, Now);
        Assert.Single(_store.Scheduled);
    }

    private sealed class FakeToastStore : INativeToastStore
    {
        public readonly Dictionary<string, (string Title, string Message, long FireAt)> Scheduled = new();
        public readonly List<string> RemovedTags = new();
        public int ClearAllCalls;
        public int UpsertCalls;

        public void ClearAll() { ClearAllCalls++; Scheduled.Clear(); }
        public void Upsert(string tag, string title, string message, long fireAtMs)
        {
            UpsertCalls++;
            Scheduled[tag] = (title, message, fireAtMs);
        }
        public void Remove(string tag)
        {
            RemovedTags.Add(tag);
            Scheduled.Remove(tag);
        }
    }
}
