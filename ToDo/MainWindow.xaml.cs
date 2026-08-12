using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ToDo.Models;
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

        // While typing (search box, inline editors, note field) editing keys must stay
        // native — Ctrl+Z/C/V are text undo/copy, and no global shortcut may hijack the
        // cursor. The inline editors handle their own Esc (Handled) before it bubbles.
        if (Keyboard.FocusedElement is TextBoxBase or PasswordBox) return;

        if (e.Key == Key.Escape)
        {
            if (ViewModel.SelectedTask != null) { ViewModel.SelectedTask = null; e.Handled = true; }
            return;
        }
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;

        switch (e.Key)
        {
            case Key.N:
                TaskListControl.FocusNewTaskBox();
                e.Handled = true;
                break;
            case Key.F:
                SidebarControl.FocusSearchBox();
                e.Handled = true;
                break;
            case Key.Enter:
                CompleteSelectedTask();
                e.Handled = true;
                break;
            case Key.Z when ViewModel.CurrentUndo != null:
                ViewModel.UndoCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.OemComma:
                ToggleSettings();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.D2:
            case Key.D3:
            case Key.D4:
                SwitchToSystemList((int)(e.Key - Key.D1) + 1);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Ctrl+Enter: complete the selected open task (recurring series generate
    /// their next instance, same as the row checkbox).</summary>
    private void CompleteSelectedTask()
    {
        var t = ViewModel.SelectedTask;
        if (t is { CloseRecord: null })
            ViewModel.CloseTaskCommand.Execute((t, CloseMode.Complete, false));
    }

    /// <summary>Ctrl+1..4: jump to a system list (My Day / Important / Planned / Tasks).</summary>
    private void SwitchToSystemList(int digit)
    {
        var id = KeyboardShortcutMap.SystemListId(digit);
        if (id != null) ViewModel.ActiveListId = id;
    }

    /// <summary>Ctrl+,: open / close the settings overlay.</summary>
    private void ToggleSettings()
    {
        if (ViewModel.IsSettingsMode) ViewModel.CloseSettingsCommand.Execute(null);
        else ViewModel.OpenSettingsCommand.Execute(null);
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
        // While actually quitting (tray "退出" / session end) or rebuilding for a
        // language change, let the window close.
        if (!WindowManager.IsQuitting && !WindowManager.IsRebuilding)
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
