using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ToDo.Models;
using ToDo.Services;

namespace ToDo;

/// <summary>
/// The always-on-top sticky-note mini window: current list's active + completed
/// (collapsed by default) tasks with a list switcher. Tasks can be completed or
/// reopened here, but not edited. Frameless note card owned by WindowManager —
/// the X button returns to the tray, the back-to-main button restores the main
/// window, and geometry persists across sessions.
/// </summary>
public partial class StickyWindow : Window
{
    private readonly DispatcherTimer _persistTimer;

    public StickyWindow()
    {
        InitializeComponent();
        DataContext = App.ViewModel;

        // Frameless window: round the corners via DWM (Win11) — no OS title bar
        // to inherit rounding from, unlike the other windows.
        SourceInitialized += (_, _) =>
            TitleBarService.RoundCorners(new WindowInteropHelper(this).Handle);

        ApplyPersistedGeometry();

        // Debounced geometry persistence so dragging/resizing doesn't hammer the disk.
        _persistTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _persistTimer.Tick += (_, _) =>
        {
            _persistTimer.Stop();
            PersistGeometry();
        };
        LocationChanged += (_, _) => RestartPersistTimer();
        SizeChanged += (_, _) => RestartPersistTimer();
        Closed += (_, _) =>
        {
            _persistTimer.Stop();
            PersistGeometry();
        };
    }

    private void RestartPersistTimer()
    {
        _persistTimer.Stop();
        _persistTimer.Start();
    }

    /// <summary>Restore the last position/size, clamped into the virtual screen so the
    /// window can't be stranded off-screen (e.g. after unplugging a monitor).</summary>
    private void ApplyPersistedGeometry()
    {
        var s = SettingsService.Current;
        if (s.StickyWidth > 0) Width = s.StickyWidth;
        if (s.StickyHeight > 0) Height = s.StickyHeight;

        // WPF exposes the virtual screen as four scalars, not a Rect.
        var vsLeft = SystemParameters.VirtualScreenLeft;
        var vsTop = SystemParameters.VirtualScreenTop;
        var vsWidth = SystemParameters.VirtualScreenWidth;
        var vsHeight = SystemParameters.VirtualScreenHeight;
        if (s.StickyLeft is double left && s.StickyTop is double top)
        {
            // Keep at least ~120px of the window reachable on either side.
            Left = Clamp(left, vsLeft - Width + 120, vsLeft + vsWidth - 120);
            Top = Clamp(top, vsTop, vsTop + vsHeight - 40);
        }
        else
        {
            Left = vsLeft + (vsWidth - Width) / 2;
            Top = vsTop + (vsHeight - Height) / 2;
        }
    }

    private static double Clamp(double value, double min, double max)
        => Math.Min(Math.Max(value, min), max);

    private void PersistGeometry()
    {
        var s = SettingsService.Current;
        s.StickyLeft = Math.Round(Left);
        s.StickyTop = Math.Round(Top);
        var width = double.IsFinite(Width) ? Width : (double.IsFinite(ActualWidth) ? ActualWidth : 340);
        var height = double.IsFinite(Height) ? Height : (double.IsFinite(ActualHeight) ? ActualHeight : 520);
        s.StickyWidth = Math.Round(width);
        s.StickyHeight = Math.Round(height);
        SettingsService.Save();
    }

    /// <summary>Header drag-to-move. The list-name text and the empty header are drag
    /// real estate; presses that land on a button (incl. the ∨ toggle) are left to it.</summary>
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;
        if (e.OriginalSource is DependencyObject source
            && (FindVisualParent<ButtonBase>(source) != null || FindVisualParent<ComboBox>(source) != null))
            return;
        DragMove();
    }

    /// <summary>Picking a list closes the dropdown; the shared ActiveListId already
    /// drives the VM, so both the sticky and the main window update.</summary>
    private void ListPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ListToggle.IsChecked = false;
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (DependencyObject? parent = VisualTreeHelper.GetParent(child); parent != null;
             parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T match) return match;
        }
        return null;
    }

    /// <summary>Back-to-main button → restore the main window.</summary>
    private void BackToMain_Click(object sender, RoutedEventArgs e) => WindowManager.ShowMain();

    /// <summary>Close button → back to the tray (main window stays hidden).</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Checkbox square click → complete the task (mirrors MainWindow.Checkbox_Click).</summary>
    private void CompleteTask_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItem task })
        {
            App.ViewModel!.CloseTaskCommand.Execute((task, CloseMode.Complete));
            e.Handled = true;
        }
    }

    /// <summary>Completed-row click → reopen the task.</summary>
    private void ReopenTask_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TaskItem task })
        {
            App.ViewModel!.ReopenTaskCommand.Execute(task);
            e.Handled = true;
        }
    }

    /// <summary>Focus triggers a sync, like the main window.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        App.Sync?.Trigger();
    }
}
