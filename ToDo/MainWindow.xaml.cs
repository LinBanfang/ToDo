using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    private DispatcherTimer? _undoTimer;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ViewModel;
        SourceInitialized += (_, _) => TitleBarService.Apply(this);
        App.ViewModel!.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTask))
                DetailPaneControl.UpdateForSelectedTask();
            if (e.PropertyName == nameof(MainViewModel.CurrentUndo))
                OnCurrentUndoChanged();
        };
        DetailPaneControl.UpdateForSelectedTask();   // defensive: pane starts collapsed, but covers a pre-set selection
    }

    /// <summary>Undo bar appears → slide in + start the 5s auto-dismiss timer; disappears →
    /// stop the timer. A new operation replacing the slot re-raises CurrentUndo, which
    /// restarts the countdown (same "newest wins" semantics as the single slot).</summary>
    private void OnCurrentUndoChanged()
    {
        _undoTimer?.Stop();
        if (ViewModel.CurrentUndo == null) return;

        _undoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _undoTimer.Tick += (_, _) => { _undoTimer.Stop(); ViewModel.ClearUndo(); };
        _undoTimer.Start();

        // Slide-in, code-behind driven (a Style-trigger EnterActions animation is known
        // not to reverse here — see TaskListControl.SetDropSlotOpen for the sibling).
        UndoBar.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        UndoBarTranslate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }


    // ─── Keyboard Shortcuts ───────────────────────────────
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && ViewModel.SelectedTask != null)
        {
            ViewModel.SelectedTask = null;
            e.Handled = true;
        }
        if (e.Key == Key.N && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            TaskListControl.FocusNewTaskBox();
            e.Handled = true;
        }
    }

    /// <summary>Window focus triggers a sync — the main "check for remote changes" moment.</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        App.Sync?.Trigger();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Flush any pending detail-pane title/note edits before exiting
        DetailPaneControl.FlushPendingEdits();

        // X closes to the tray (default); with the toggle off it exits the app.
        // While actually quitting (tray "退出" / session end) let the window close.
        if (!WindowManager.IsQuitting)
        {
            e.Cancel = true;
            if (SettingsService.Current.MinimizeToTrayOnClose)
                Hide();
            else
                WindowManager.Quit();
        }
        base.OnClosing(e);
    }
}
