using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;
using System.Windows.Threading;
using ToDo.Models;

namespace ToDo.Services;

/// <summary>
/// Periodically checks tasks for due reminders and raises a system notification
/// (tray balloon) plus a sound, once per reminder. Reminders that were already due
/// before this session started are skipped so the app doesn't nag on launch.
/// </summary>
public class ReminderService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly NotifyIcon _trayIcon;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<string> _fired = new();
    private bool _disposed;

    public ReminderService(DatabaseService db)
    {
        _db = db;

        // Pre-mark reminders that are already due so they don't all fire at startup
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var t in db.Tasks.FindAll())
        {
            if (t.Reminder != null && t.Reminder <= now)
                _fired.Add($"{t.Id}|{t.Reminder}");
        }

        _trayIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = "To Do",
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return Icon.ExtractAssociatedIcon(exe) ?? SystemIcons.Application;
        }
        catch { }
        return SystemIcons.Application;
    }

    private void Check()
    {
        if (_disposed) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<TaskItem> due;
        try
        {
            due = _db.Tasks.FindAll()
                .Where(t => t.Reminder != null && t.Reminder <= now && t.CloseRecord == null)
                .ToList();
        }
        catch { return; }

        foreach (var t in due)
        {
            var key = $"{t.Id}|{t.Reminder}";
            if (_fired.Add(key))
            {
                _trayIcon.ShowBalloonTip(5000, Loc.Reminder, t.Title, ToolTipIcon.Info);
                SystemSounds.Exclamation.Play();
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _trayIcon.Dispose();
    }
}
