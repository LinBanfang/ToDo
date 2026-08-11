using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ToDo.Models;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises ReminderService's dedup / prune state machine: already-due reminders are
/// pre-marked so the app doesn't nag on launch, a reminder fires at most once, and
/// completing / reopening or rescheduling a task lets its reminder fire again in-session.
/// The toast and sound side effects are gated behind SettingsService toggles, which the
/// tests keep off — the observable state is the private _fired key set.
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

    private static HashSet<string> Fired(ReminderService svc) =>
        (HashSet<string>)typeof(ReminderService).GetField("_fired", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(svc)!;

    private static void Check(ReminderService svc) =>
        typeof(ReminderService).GetMethod("Check", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(svc, null);

    private static string ResolveListIcon(ReminderService svc, TaskItem t) =>
        (string)typeof(ReminderService).GetMethod("ResolveListIcon", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(svc, new object[] { t })!;

    [Fact]
    public void Constructor_PreMarksAlreadyDueOpenReminders_ButNotFutureOrClosed()
    {
        var dueRem = Past();
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = dueRem });
        _db.Tasks.Insert(new TaskItem { Id = "future", ListId = "list-tasks", Reminder = Future() });
        _db.Tasks.Insert(new TaskItem
        {
            Id = "closed",
            ListId = "list-tasks",
            Reminder = Past(),
            CloseRecord = new CloseRecord { ClosedAt = Past() },
        });

        using var svc = new ReminderService(_db);
        var fired = Fired(svc);

        Assert.Contains($"due|{dueRem}", fired);       // open + already due → skipped at startup
        Assert.DoesNotContain(fired, f => f.StartsWith("future|"));   // not yet due → not marked
        Assert.DoesNotContain(fired, f => f.StartsWith("closed|"));    // closed → not marked
    }

    [Fact]
    public void Check_FiresNewlyDueReminder_ExactlyOnce()
    {
        using var svc = new ReminderService(_db);
        Assert.Empty(Fired(svc));

        var rem = Past();
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem });

        Check(svc);
        Assert.Contains($"due|{rem}", Fired(svc));     // fired → key recorded

        Check(svc);
        Assert.Single(Fired(svc));                     // second poll: key still eligible → no re-fire
    }

    [Fact]
    public void Check_CompletedThenReopened_FiresAgain()
    {
        var rem = Past();
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = rem });
        using var svc = new ReminderService(_db);
        Assert.Contains($"due|{rem}", Fired(svc));     // pre-marked on launch

        // Complete it → the marker is pruned so a later reopen can fire again.
        var t = _db.Tasks.FindById("due")!;
        t.CloseRecord = new CloseRecord { ClosedAt = Past() };
        _db.Tasks.Update(t);
        Check(svc);
        Assert.DoesNotContain($"due|{rem}", Fired(svc));

        // Reopen → fires again (this is the v1.2.1 "提醒重复触发" regression).
        t.CloseRecord = null;
        _db.Tasks.Update(t);
        Check(svc);
        Assert.Contains($"due|{rem}", Fired(svc));
    }

    [Fact]
    public void Check_RescheduledReminder_PrunesOldKeyAndFiresNewOne()
    {
        var oldRem = Past();
        _db.Tasks.Insert(new TaskItem { Id = "due", ListId = "list-tasks", Reminder = oldRem });
        using var svc = new ReminderService(_db);
        Assert.Contains($"due|{oldRem}", Fired(svc));

        // Push the reminder to another (still past) time → the old marker must not block it.
        var newRem = Past();
        var t = _db.Tasks.FindById("due")!;
        t.Reminder = newRem;
        _db.Tasks.Update(t);
        Check(svc);

        Assert.DoesNotContain($"due|{oldRem}", Fired(svc));
        Assert.Contains($"due|{newRem}", Fired(svc));
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
}
