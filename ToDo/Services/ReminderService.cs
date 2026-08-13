using System.Windows.Threading;
using ToDo.Models;
using ToDo.Views;

namespace ToDo.Services;

/// <summary>
/// Periodically checks tasks for due reminders and raises an in-app Fluent toast
/// (bottom-right card, replaces the WinForms balloon that Windows 11 no longer shows)
/// plus a sound, once per reminder. Catch-up strategy: only reminders older than the
/// catch-up window (default 24h) are pre-marked so the app doesn't nag on launch;
/// reminders that came due within the window fire on the first poll instead.
/// Also keeps the native scheduled toasts in sync (optional, see
/// <see cref="INativeReminderScheduler"/>): a reminder that never fired in-app because
/// the app was closed still notifies via an OS-delivered toast.
/// </summary>
public class ReminderService : IDisposable
{
    private const double DefaultCatchUpHours = 24;

    private readonly DatabaseService _db;
    private readonly DispatcherTimer _timer;
    private readonly INativeReminderScheduler? _native;
    private bool _disposed;

    public ReminderService(DatabaseService db, TimeSpan? catchUpWindow = null,
        INativeReminderScheduler? nativeScheduler = null)
    {
        _db = db;
        _native = nativeScheduler;

        // Pre-mark only reminders older than the catch-up window — anything due within it
        // is left for the first Check() (15s later) to fire, so a reminder missed by a few
        // hours still nags once. Only open tasks are marked (mirroring the poll filter).
        // FiredReminder (ADR-019) makes the "already fired" state durable and synced, so a
        // stale reminder doesn't re-nag here or on another device; setting it through the
        // tracked Update is a one-time, idempotent write (the guard skips already-fired rows).
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var window = (catchUpWindow ?? TimeSpan.FromHours(DefaultCatchUpHours)).TotalMilliseconds;
        foreach (var t in db.Tasks.Find(t =>
                     t.Reminder != null && t.Reminder < now - window && t.CloseRecord == null && t.FiredReminder != t.Reminder))
        {
            t.FiredReminder = t.Reminder;
            db.Tasks.Update(t);
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        if (_disposed) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<TaskItem> due;
        try
        {
            // Uses the Reminder index, so it doesn't scan the whole table every 15s
            due = _db.Tasks.Find(t => t.Reminder != null && t.Reminder <= now && t.CloseRecord == null)
                .ToList();
        }
        catch (Exception ex)
        {
            // Never let a query failure silently kill the reminder feature.
            DiagnosticLog.Error("reminder", $"poll failed: {ex}");
            return;
        }

        foreach (var t in due)
        {
            // FiredReminder (ADR-019) is the durable, cross-device "already fired" marker: a
            // task that fired its current reminder — here or on another device — is skipped.
            // Rescheduling (Reminder changed) makes FiredReminder != Reminder → fires again;
            // clearing it on close makes a reopen fire again.
            if (t.FiredReminder == t.Reminder) continue;

            t.FiredReminder = t.Reminder;
            _db.Tasks.Update(t);   // tracked → stamps ModifiedAt + outbox, syncing the fired state

            // Respect the settings toggles on each poll so changes apply live
            if (SettingsService.Current.ReminderNotifications)
                ReminderToast.Show(t.Id, t.Title, ResolveListIcon(t));
            if (SettingsService.Current.ReminderSound)
                ReminderSoundPlayer.Play();

            // The in-app card is the notification while the app runs — drop the
            // pending native toast so the OS doesn't fire it again moments later.
            if (t.Reminder is long r) _native?.RemoveFired(t.Id, r);
        }

        SyncNativeSchedule(now);
    }

    /// <summary>
    /// Every poll, re-align the OS-scheduled native toasts with the open future
    /// reminders (adding ones that appeared since, dropping ones that were deleted or
    /// completed). With notifications disabled the whole native schedule is cleared.
    /// Failures are logged but never break the poll loop.
    /// </summary>
    private void SyncNativeSchedule(long now)
    {
        if (_native == null) return;
        try
        {
            if (SettingsService.Current.ReminderNotifications)
            {
                var open = _db.Tasks.Find(t => t.Reminder != null && t.CloseRecord == null).ToList();
                _native.Reconcile(open, now);
            }
            else
            {
                _native.ClearAll();
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Error("reminder", $"native schedule failed: {ex}");
        }
    }

    /// <summary>
    /// Emoji of the list the task belongs to (the same icon shown in the sidebar and
    /// list header), so a toast is recognizable at a glance. Falls back to a generic
    /// task glyph when the list can't be resolved or carries no icon.
    /// </summary>
    private string ResolveListIcon(TaskItem t)
    {
        var list = _db.Lists.FindById(t.ListId);
        return list is { Icon.Length: > 0 } ? list.Icon : DefaultTaskIcon;
    }

    private const string DefaultTaskIcon = "📝";

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
    }
}
