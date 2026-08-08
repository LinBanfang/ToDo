using System.Windows.Threading;
using ToDo.Models;
using ToDo.Views;

namespace ToDo.Services;

/// <summary>
/// Periodically checks tasks for due reminders and raises an in-app Fluent toast
/// (bottom-right card, replaces the WinForms balloon that Windows 11 no longer shows)
/// plus a sound, once per reminder. Reminders that were already due before this session
/// started are skipped so the app doesn't nag on launch.
/// </summary>
public class ReminderService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _fired = new();
    private bool _disposed;

    public ReminderService(DatabaseService db)
    {
        _db = db;

        // Pre-mark reminders that are already due so they don't all fire at startup.
        // Only open tasks are marked (mirroring the poll filter): a task completed before
        // shutdown must be able to fire again when the user reopens it this session.
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var t in db.Tasks.Find(t => t.Reminder != null && t.Reminder <= now && t.CloseRecord == null))
        {
            _fired.Add($"{t.Id}|{t.Reminder}");
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

        // "Fired" markers only apply to tasks that are still open-and-due. Prune markers
        // of completed / deleted / rescheduled tasks so completing and reopening a task
        // lets its reminder fire again in this session, instead of a stale key blocking it.
        var eligible = new HashSet<string>(due.Select(t => $"{t.Id}|{t.Reminder}"));
        _fired.RemoveWhere(key => !eligible.Contains(key));

        foreach (var t in due)
        {
            var key = $"{t.Id}|{t.Reminder}";
            if (_fired.Add(key))
            {
                // Respect the settings toggles on each poll so changes apply live
                if (SettingsService.Current.ReminderNotifications)
                    ReminderToast.Show(t.Title, ResolveListIcon(t));
                if (SettingsService.Current.ReminderSound)
                    ReminderSoundPlayer.Play();
            }
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
