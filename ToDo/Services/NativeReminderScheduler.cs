using CommunityToolkit.WinUI.Notifications;
using ToDo.Models;
using Windows.UI.Notifications;

namespace ToDo.Services;

/// <summary>
/// Thin adapter over the Windows toast platform. The real implementation
/// (<see cref="WinToastStore"/>) uses CommunityToolkit's compat layer, which
/// registers the unpackaged app's AUMID automatically (exe-path based — the
/// scheduled toast is delivered by the OS even when the app is closed). Tests
/// substitute a fake so the reconcile logic stays unit-testable without WinRT.
/// </summary>
public interface INativeToastStore
{
    /// <summary>Drop every toast in the app's reminder group (startup residue
    /// cleanup and the "notifications off" path).</summary>
    void ClearAll();

    /// <summary>Schedule a toast that fires at <paramref name="fireAtMs"/> (unix
    /// millis). Called once per tag thanks to the scheduler's mirror diff.</summary>
    void Upsert(string tag, string title, string message, long fireAtMs);

    /// <summary>Remove the scheduled toast with the given tag, if any.</summary>
    void Remove(string tag);
}

/// <summary>
/// Keeps the OS's scheduled native toasts in sync with the app's open reminders.
/// Piggybacks on <see cref="ReminderService"/>'s poll tick. The in-app card and the
/// native toast are mutually exclusive per reminder: while the app runs, the in-app
/// card fires at the due time and <see cref="RemoveFired"/> drops + suppresses the
/// pending native toast (scheduled at due + grace so a just-firing reminder never
/// double-notifies); when the app is closed the OS fires the native toast instead.
/// Null-safe: <see cref="ReminderService"/> treats a missing scheduler as a no-op.
/// </summary>
public interface INativeReminderScheduler
{
    /// <summary>Diff the desired schedule (open, future reminders) against the
    /// in-memory mirror and push only the deltas to the store.</summary>
    void Reconcile(IEnumerable<TaskItem> openTasks, long nowMs);

    /// <summary>A reminder was notified in-app; drop + suppress its pending native
    /// toast so the OS doesn't fire it within the grace window.</summary>
    void RemoveFired(string taskId, long reminderMs);

    /// <summary>Clear every scheduled native toast (used when notifications are off).</summary>
    void ClearAll();
}

/// <summary>
/// Pure reconcile logic over <see cref="INativeToastStore"/>. A toast is scheduled
/// for each open task's reminder at <c>reminder + grace</c> — the grace is a buffer
/// larger than the 15s poll so the in-app card always wins when the app is running.
/// </summary>
public sealed class NativeReminderScheduler : INativeReminderScheduler
{
    private const string Group = "todo-reminder";

    private readonly INativeToastStore _store;
    private readonly long _graceMs;
    private readonly Dictionary<string, long> _mirror = new();     // key -> fireAt (unix ms)
    private readonly Dictionary<string, long> _suppressed = new(); // key -> reminderMs

    public NativeReminderScheduler(INativeToastStore store, TimeSpan? grace = null)
    {
        _store = store;
        _graceMs = (long)(grace ?? TimeSpan.FromSeconds(45)).TotalMilliseconds;

        // Drop toasts left over from a previous session (the mirror below is empty,
        // so a plain diff would never notice them) and rebuild from current state.
        _store.ClearAll();
    }

    private static string Key(TaskItem t, long reminderMs) => $"{t.Id}|{reminderMs}";

    public void Reconcile(IEnumerable<TaskItem> openTasks, long nowMs)
    {
        // Once the native fire time has passed, a suppressed reminder can never be
        // re-added — prune it so the bookkeeping doesn't grow unboundedly.
        foreach (var key in _suppressed.Where(kv => kv.Value + _graceMs <= nowMs)
                     .Select(kv => kv.Key).ToList())
            _suppressed.Remove(key);

        var desired = new Dictionary<string, (TaskItem Task, long FireAt)>();
        foreach (var t in openTasks)
        {
            if (t.CloseRecord != null || t.Reminder is not long r) continue;
            if (t.FiredReminder == t.Reminder) continue; // already fired (this/another device) → suppress (ADR-019)
            var fireAt = r + _graceMs;
            if (fireAt <= nowMs) continue;               // already due → in-app catch-up owns it
            var key = Key(t, r);
            if (_suppressed.ContainsKey(key)) continue;  // fired in-app within the grace window
            desired[key] = (t, fireAt);
        }

        foreach (var (key, entry) in desired)
        {
            if (_mirror.TryGetValue(key, out var fireAt) && fireAt == entry.FireAt) continue;
            _store.Upsert(key, entry.Task.Title, Loc.Reminder, entry.FireAt);
            _mirror[key] = entry.FireAt;
        }

        foreach (var key in _mirror.Keys.Where(k => !desired.ContainsKey(k)).ToList())
        {
            _store.Remove(key);
            _mirror.Remove(key);
        }
    }

    public void RemoveFired(string taskId, long reminderMs)
    {
        var key = $"{taskId}|{reminderMs}";
        if (_mirror.Remove(key)) _store.Remove(key);
        _suppressed[key] = reminderMs;
    }

    public void ClearAll()
    {
        _store.ClearAll();
        _mirror.Clear();
        _suppressed.Clear();
    }
}

/// <summary>
/// Real <see cref="INativeToastStore"/>: schedules toasts through
/// <see cref="ToastNotificationManagerCompat"/>, which auto-registers the unpackaged
/// app's AUMID on first use. All toasts carry the <c>todo-reminder</c> group so a
/// session restart can drop the residue in one pass.
/// </summary>
public sealed class WinToastStore : INativeToastStore
{
    private const string Group = "todo-reminder";

    private readonly ToastNotifierCompat _notifier;

    public WinToastStore()
    {
        // First call registers the AUMID (exe-path based) for this unpackaged app;
        // subsequent calls reuse it. Throws only if toast notifications are broken
        // system-wide — the scheduler's caller logs and moves on.
        _notifier = ToastNotificationManagerCompat.CreateToastNotifier();
    }

    public void Upsert(string tag, string title, string message, long fireAtMs)
    {
        var content = new ToastContentBuilder()
            .AddText(title)
            .AddText(message)
            .GetXml();
        var scheduled = new ScheduledToastNotification(
            content, DateTimeOffset.FromUnixTimeMilliseconds(fireAtMs))
        {
            Tag = tag,
            Group = Group,
        };
        _notifier.AddToSchedule(scheduled);
    }

    public void Remove(string tag)
    {
        foreach (var s in _notifier.GetScheduledToastNotifications())
        {
            if (s.Tag == tag)
            {
                _notifier.RemoveFromSchedule(s);
                return;
            }
        }
    }

    public void ClearAll()
    {
        foreach (var s in _notifier.GetScheduledToastNotifications())
        {
            if (s.Group == Group) _notifier.RemoveFromSchedule(s);
        }
    }
}
