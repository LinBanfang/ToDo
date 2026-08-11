using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private bool _suppressDetailEvents;
    private bool _suppressTaskClick;
    private Point _dragStartPoint;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ViewModel;
        SourceInitialized += (_, _) => TitleBarService.Apply(this);
        App.ViewModel!.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTask))
                UpdateDetailPane();
        };
        UpdateDetailPane();   // defensive: pane starts collapsed, but covers a pre-set selection

        SearchBox.TextChanged += (s, e) =>
        {
            var hasText = !string.IsNullOrEmpty(SearchBox.Text);
            SearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            SearchClearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        };
        UpdateAddTaskPlaceholder();
    }


    // ─── Dialogs ──────────────────────────────────────────
    private void OpenEditCloseTimeDialog(TaskItem task)
    {
        if (task.CloseRecord == null) return;
        var dialog = new DateTimeDialog(task.CloseRecord.ClosedAt) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Saved)
            ViewModel.EditCloseTimeCommand.Execute((task, dialog.ResultTimestamp));
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
            AddTaskBox.Focus();
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
        DetailTitleBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        DetailNoteBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (ViewModel.SelectedTask != null)
            ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);

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
