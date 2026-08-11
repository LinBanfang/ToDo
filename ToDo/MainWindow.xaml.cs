using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ViewModel;
        SourceInitialized += (_, _) => TitleBarService.Apply(this);
        App.ViewModel!.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTask))
                DetailPaneControl.UpdateForSelectedTask();
        };
        DetailPaneControl.UpdateForSelectedTask();   // defensive: pane starts collapsed, but covers a pre-set selection
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
