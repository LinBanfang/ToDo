using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ToDo.Models;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises ReminderService's dedup / fire state machine (ADR-019): already-due reminders
/// are pre-marked so the app doesn't nag on launch, a reminder fires at most once, and
/// rescheduling or reopening a task lets its reminder fire again. The observable state is
/// the persisted TaskItem.FiredReminder — the toast / sound side effects are gated off via
/// the settings toggles.
/// </summary>
[Collection("settings-shared")]   // serialized with the other SettingsService users
public sealed class ReminderServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly DatabaseService _db;

    public ReminderServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "todo-reminder-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        SettingsService.UseDirectory(_dir);
        // Keep the poll side-effects inert — the WPF toast / SoundPlayer aren't testable here.
        SettingsService.Current.ReminderNotifications = false;
        SettingsService.Current.ReminderSound = false;
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static long Past() => DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
    private static long Future() => DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();
    // Older than the default 24h catch-up window, so ctor pre-marks it (silent on launch).
    private static long LongPast() => DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeMilliseconds();

    private long? Fired(string id) => _db.Tasks.FindById(id)!.FiredReminder;

    private static void Check(ReminderService svc) =>
        typeof(ReminderService).GetMethod("Check", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(svc, null);

    private static string ResolveListIcon(ReminderService svc, TaskItem t) =>
        (string)typeof(ReminderService).GetMethod("ResolveListIcon", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(svc, new object[] { t })!;

    [Fact]
    public void Constructor_PreMarksAlreadyDueOpenReminders_ButNotFutureOrClosed()
    {
        var dueRem = LongPast();   // older than the catch-up window → silent at startup
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = dueRem });
        _db.Tasks.Insert(new TaskItem { Id = "future", ListId = "list-tasks", Reminder = Future() });
        _db.Tasks.Insert(new TaskItem
        {
            Id = "closed",
            ListId = "list-tasks",
            Reminder = LongPast(),
            CloseRecord = new CloseRecord { ClosedAt = Past() },
        });

        using var svc = new ReminderService(_db);

        Assert.Equal(dueRem, Fired("due"));        // open + already due → skipped at startup
        Assert.Null(Fired("future"));              // not yet due → not marked
        Assert.Null(Fired("closed"));              // closed → not marked
    }

    [Fact]
    public void Check_FiresNewlyDueReminder_ExactlyOnce()
    {
        var native = new FakeNativeScheduler();
        using var svc = new ReminderService(_db, nativeScheduler: native);

        var rem = Past();
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem });
        Assert.Null(Fired("due"));

        Check(svc);
        Assert.Equal(rem, Fired("due"));           // fired → marker persisted
        Assert.Single(native.Fired);               // pending native toast dropped once

        Check(svc);
        Assert.Equal(rem, Fired("due"));           // second poll: no re-fire
        Assert.Single(native.Fired);               // ...and no second native drop
    }

    [Fact]
    public void Check_CompletedThenReopened_FiresAgain()
    {
        var rem = LongPast();   // outside the window → pre-marked on launch (asserted below)
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem });
        using var svc = new ReminderService(_db);
        Assert.Equal(rem, Fired("due"));           // pre-marked on launch

        // Complete it — CloseTask clears FiredReminder (mirrored directly here) so a later
        // reopen can fire again.
        var t = _db.Tasks.FindById("due")!;
        t.CloseRecord = new CloseRecord { ClosedAt = Past() };
        t.FiredReminder = null;
        _db.Tasks.Update(t);
        Check(svc);
        Assert.Null(Fired("due"));                 // cleared by close; closed → not re-fired

        // Reopen → fires again (this is the v1.2.1 "提醒重复触发" regression).
        t.CloseRecord = null;
        _db.Tasks.Update(t);
        Check(svc);
        Assert.Equal(rem, Fired("due"));
    }

    [Fact]
    public void Check_RescheduledReminder_FiresNewOne()
    {
        var oldRem = LongPast();   // outside the window → pre-marked on launch (asserted below)
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = oldRem });
        using var svc = new ReminderService(_db);
        Assert.Equal(oldRem, Fired("due"));

        // Push the reminder to another (still past) time → FiredReminder != new Reminder,
        // so the new value fires.
        var newRem = Past();
        var t = _db.Tasks.FindById("due")!;
        t.Reminder = newRem;
        _db.Tasks.Update(t);
        Check(svc);

        Assert.Equal(newRem, Fired("due"));        // marker advanced to the new reminder
    }

    [Fact]
    public void Check_AlreadyFiredReminder_DoesNotFire()
    {
        // A task synced from another device that already fired its reminder (ADR-019).
        var native = new FakeNativeScheduler();
        var rem = Past();
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem, FiredReminder = rem });
        using var svc = new ReminderService(_db, nativeScheduler: native);

        Check(svc);

        Assert.Empty(native.Fired);                // already fired elsewhere → suppressed
    }

    // ─── Catch-up window (v1.3.2): only reminders older than the window are pre-marked
    // at startup; within-window ones fire on the first poll instead of being nagged early
    // or skipped entirely. ─────────────────────────────────────

    [Fact]
    public void Ctor_DueReminderWithinWindow_NotPreMarked()
    {
        var rem = Past();   // now - 5min, inside the default 24h window
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem });

        using var svc = new ReminderService(_db);

        Assert.Null(Fired("due"));   // left for the first poll
    }

    [Fact]
    public void Ctor_DueReminderOlderThanWindow_PreMarked()
    {
        var rem = LongPast();   // now - 25h, outside the window
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem });

        using var svc = new ReminderService(_db);

        Assert.Equal(rem, Fired("due"));   // silent — no startup nag
    }

    [Fact]
    public void Check_FiresWithinWindowReminder_Once()
    {
        // Seeded BEFORE the ctor but within the window → not pre-marked, so the first
        // poll fires it exactly once and a second poll does not re-fire.
        var native = new FakeNativeScheduler();
        var rem = Past();
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem });
        using var svc = new ReminderService(_db, nativeScheduler: native);
        Assert.Null(Fired("due"));

        Check(svc);
        Assert.Equal(rem, Fired("due"));
        Assert.Single(native.Fired);

        Check(svc);
        Assert.Single(native.Fired);               // no re-fire
    }

    [Theory]
    [InlineData(2, true)]    // window 2min < age 5min → older than window → pre-marked
    [InlineData(60, false)]  // window 60min > age 5min → within window → not pre-marked
    public void Ctor_ParameterizedCatchUpWindow(double windowMinutes, bool expectedPreMarked)
    {
        var rem = Past();
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem });

        using var svc = new ReminderService(_db, TimeSpan.FromMinutes(windowMinutes));

        if (expectedPreMarked)
            Assert.Equal(rem, Fired("due"));
        else
            Assert.Null(Fired("due"));
    }

    [Fact]
    public void ResolveListIcon_FallsBackToDefault_WhenListMissing()
    {
        using var svc = new ReminderService(_db);
        var t = new TaskItem { Id = "t", ListId = "no-such-list", Title = "x" };
        Assert.Equal("📝", ResolveListIcon(svc, t));
    }

    [Fact]
    public void ResolveListIcon_UsesListIcon_WhenPresent()
    {
        _db.Lists.Insert(new TaskList { Id = "list-emoji", Name = "L", Icon = "🎯" });
        using var svc = new ReminderService(_db);
        var t = new TaskItem { Id = "t", ListId = "list-emoji", Title = "x" };
        Assert.Equal("🎯", ResolveListIcon(svc, t));
    }

    [Fact]
    public void ResolveListIcon_FallsBackToDefault_WhenListIconEmpty()
    {
        _db.Lists.Insert(new TaskList { Id = "list-empty", Name = "L", Icon = "" });
        using var svc = new ReminderService(_db);
        var t = new TaskItem { Id = "t", ListId = "list-empty", Title = "x" };
        Assert.Equal("📝", ResolveListIcon(svc, t));
    }

    [Fact]
    public void Dispose_StopsTimer_AndCheckBecomesNoOp()
    {
        var svc = new ReminderService(_db);
        svc.Dispose();
        // Must not throw even though a poll fires against the disposed service.
        Check(svc);
    }

    // ─── Native toast sync (P2-6): the in-app card and the OS-scheduled toast are
    // mutually exclusive per reminder; the scheduler is only invoked when provided. ──

    [Fact]
    public void Check_FiresDueReminder_RemovesPendingNativeToast()
    {
        var native = new FakeNativeScheduler();
        using var svc = new ReminderService(_db, nativeScheduler: native);
        var rem = Past();   // within the 24h window → not pre-marked → first poll fires
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Title = "Due", Reminder = rem });

        Check(svc);

        Assert.Contains(native.Fired, f => f.TaskId == "due" && f.ReminderMs == rem);
    }

    [Fact]
    public void Check_WithNotificationsOn_ReconcilesOpenReminders()
    {
        SettingsService.Current.ReminderNotifications = true;
        try
        {
            var native = new FakeNativeScheduler();
            using var svc = new ReminderService(_db, nativeScheduler: native);
            var rem = Future();   // not due → poll won't fire the WPF toast, only reconcile
            _db.Tasks.Insert(new TaskItem { Id = "future", ListId = "list-tasks", Title = "F", Reminder = rem });

            Check(svc);

            var open = Assert.Single(native.Reconciles).Open;
            Assert.Contains(open, t => t.Id == "future" && t.Reminder == rem);
        }
        finally
        {
            SettingsService.Current.ReminderNotifications = false;
        }
    }

    [Fact]
    public void Check_WithNotificationsOff_ClearsNativeSchedule()
    {
        var native = new FakeNativeScheduler();
        using var svc = new ReminderService(_db, nativeScheduler: native);

        Check(svc);

        Assert.True(native.ClearAllCalls >= 1);   // disabled → no native toasts may fire
    }

    private sealed class FakeNativeScheduler : INativeReminderScheduler
    {
        public readonly List<(string TaskId, long ReminderMs)> Fired = new();
        public readonly List<(List<TaskItem> Open, long Now)> Reconciles = new();
        public int ClearAllCalls;

        public void Reconcile(IEnumerable<TaskItem> openTasks, long nowMs) =>
            Reconciles.Add((openTasks.ToList(), nowMs));
        public void RemoveFired(string taskId, long reminderMs) => Fired.Add((taskId, reminderMs));
        public void ClearAll() => ClearAllCalls++;
    }
}
