using System.Media;
using System.Windows.Forms;
using System.Windows.Threading;
using ToDo.Models;

namespace ToDo.Services;

/// <summary>
/// Periodically checks tasks for due reminders and raises a system notification
/// (tray balloon) plus a sound, once per reminder. Reminders that were already due
/// before this session started are skipped so the app doesn't nag on launch.
/// The tray icon is owned by <see cref="TrayService"/> and shared with this service.
/// </summary>
public class ReminderService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly NotifyIcon _trayIcon;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _fired = new();
    private bool _disposed;

    public ReminderService(DatabaseService db, NotifyIcon trayIcon)
    {
        _db = db;
        _trayIcon = trayIcon;

        // Pre-mark reminders that are already due so they don't all fire at startup
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var t in db.Tasks.Find(t => t.Reminder != null && t.Reminder <= now))
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
        catch { return; }

        foreach (var t in due)
        {
            var key = $"{t.Id}|{t.Reminder}";
            if (_fired.Add(key))
            {
                // Respect the settings toggles on each poll so changes apply live
                if (SettingsService.Current.ReminderNotifications)
                    _trayIcon.ShowBalloonTip(5000, Loc.Reminder, t.Title, ToolTipIcon.Info);
                if (SettingsService.Current.ReminderSound)
                    SystemSounds.Exclamation.Play();
            }
        }
    }

    public void Dispose()
    {
        // The tray icon is owned and disposed by TrayService; we only stop the timer
        // so no balloon fires against a disposed icon during shutdown.
        _disposed = true;
        _timer.Stop();
    }
}
