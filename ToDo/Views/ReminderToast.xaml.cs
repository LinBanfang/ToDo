using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ToDo.Services;

namespace ToDo.Views;

/// <summary>
/// Bottom-right Fluent toast shown for a due reminder. Replaces the WinForms tray
/// balloon, which Windows 11 no longer displays. Auto-dismisses after a few seconds,
/// stacks above any already-open toast, and clicking it brings the app forward.
/// </summary>
public partial class ReminderToast : Window
{
    private const double Gap = 12;

    /// <summary>Open toasts, so each new one sits above the previous.</summary>
    private static readonly List<ReminderToast> Open = new();

    public ReminderToast(string message, string icon)
    {
        InitializeComponent();
        MessageText.Text = message;
        IconText.Text = icon;
    }

    /// <summary>Shows a reminder toast for one task (caller owns lifetime via the stack).
    /// <paramref name="icon"/> is the task's list emoji, resolved by the caller.</summary>
    public static void Show(string message, string icon)
    {
        var toast = new ReminderToast(message, icon);
        Open.Add(toast);
        toast.PopUp();
    }

    private void PopUp()
    {
        Show();
        Position();   // after Show(), SizeToContent has produced the real size
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); Dismiss(); };
        timer.Start();
    }

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
