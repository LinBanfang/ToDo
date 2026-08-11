using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ToDo.Services;

namespace ToDo.Views;

/// <summary>
/// Bottom-right Fluent toast shown for a due reminder. Replaces the WinForms tray
/// balloon, which Windows 11 no longer displays. Auto-dismisses after
/// SettingsService.ReminderToastSeconds (0 = keep open until clicked); hovering the
/// card pauses the countdown. Stacks above any already-open toast, and clicking it
/// brings the app forward.
/// </summary>
public partial class ReminderToast : Window
{
    private const double Gap = 12;

    /// <summary>Open toasts, so each new one sits above the previous.</summary>
    private static readonly List<ReminderToast> Open = new();

    private readonly string _taskId;
    private DispatcherTimer? _autoCloseTimer;
    private bool _dismissing;

    public ReminderToast(string taskId, string title, string icon)
    {
        InitializeComponent();
        _taskId = taskId;
        MessageText.Text = title;
        IconText.Text = icon;
    }

    /// <summary>Shows a reminder toast for one task (caller owns lifetime via the stack).
    /// <paramref name="taskId"/> routes the action buttons to the ViewModel; <paramref name="icon"/>
    /// is the task's list emoji, resolved by the caller.</summary>
    public static void Show(string taskId, string title, string icon)
    {
        var toast = new ReminderToast(taskId, title, icon);
        Open.Add(toast);
        toast.PopUp();
    }

    /// <summary>Stop a button click from bubbling up to the card's Toast_Click (which would
    /// dismiss + bring the app forward on top of the button's own action). ButtonBase fires
    /// Click first; marking the mouse-up handled here keeps the bubble from reaching the Border.</summary>
    private void Button_SuppressToastClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        App.ViewModel?.SnoozeReminder(_taskId);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        App.ViewModel?.OpenReminderTask(_taskId);
        WindowManager.ShowMain();   // bring the app forward so the user sees the selected task
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        App.ViewModel?.CompleteReminderTask(_taskId);
    }

    private void PopUp()
    {
        Show();
        Position();   // after Show(), SizeToContent has produced the real size
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        _autoCloseTimer = new DispatcherTimer();
        _autoCloseTimer.Tick += (_, _) => { _autoCloseTimer!.Stop(); Dismiss(); };
        // Defer one dispatcher tick so IsMouseOver reflects the real pointer once the
        // window is up; if the mouse already rests on the card, MouseLeave arms instead.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => { if (!IsMouseOver) ArmAutoClose(); });
    }

    /// <summary>(Re)start the auto-close countdown for the configured duration — unless
    /// disabled (0 = keep open until clicked) or a dismiss is already running. Called on
    /// MouseLeave so a hovered-then-left card starts over with the full duration.</summary>
    private void ArmAutoClose()
    {
        if (_dismissing) return;
        var seconds = SettingsService.Current.ReminderToastSeconds;
        if (seconds <= 0) { _autoCloseTimer?.Stop(); return; }
        _autoCloseTimer!.Interval = TimeSpan.FromSeconds(seconds);
        _autoCloseTimer.Start();
    }

    /// <summary>Hovering pauses the countdown (it re-arms from the full duration on leave).</summary>
    private void Toast_MouseEnter(object sender, MouseEventArgs e) => _autoCloseTimer?.Stop();

    private void Toast_MouseLeave(object sender, MouseEventArgs e) => ArmAutoClose();

    /// <summary>Anchor to the bottom-right of the work area, above any open toast.
    /// Uses the measured size (Actual*) since the height adapts to the content.</summary>
    private void Position()
    {
        var wa = SystemParameters.WorkArea;
        double w = ActualWidth, h = ActualHeight;
        Left = wa.Right - w - 16;
        double top = wa.Bottom - h - 16;
        foreach (var other in Open)
        {
            if (other == this) continue;
            top = Math.Min(top, other.Top - h - Gap);   // sit above the highest existing
        }
        Top = Math.Max(wa.Top + 8, top);
    }

    private void Toast_Click(object sender, RoutedEventArgs e)
    {
        Dismiss();
        WindowManager.ShowMain();   // bring the app forward so the user can act on it
    }

    private void Dismiss()
    {
        if (_dismissing) return;   // timer, button and card-click all funnel here
        _dismissing = true;
        _autoCloseTimer?.Stop();
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(250));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Open.Remove(this);
    }
}
