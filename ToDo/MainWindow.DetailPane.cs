using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo;

public partial class MainWindow
{
    // ─── Detail Pane ──────────────────────────────────────

    private const double DetailPaneMinWidth = 240;
    private const double DetailPaneMaxWidth = 560;

    private bool _detailResizing;
    private double _detailResizeStartWidth;
    private double _detailResizeStartX;

    /// <summary>Opens/closes the detail pane with a width slide so the themed task area
    /// re-crops smoothly instead of snapping (the horizontal shift when the column
    /// appears/disappears). The slide targets the persisted width (SettingsService.
    /// DetailPaneWidth), which the pane's splitter can also resize directly. Content binds to
    /// the pane's DataContext — a snapshot of the task — so it stays rendered during slide-out
    /// instead of going blank.</summary>
    private void UpdateDetailPane()
    {
        var task = ViewModel.SelectedTask;
        var target = Math.Clamp(SettingsService.Current.DetailPaneWidth, DetailPaneMinWidth, DetailPaneMaxWidth);
        if (task == null)
        {
            if (DetailPane.Visibility == Visibility.Visible)
            {
                DetailSplitter.Visibility = Visibility.Collapsed;   // no lone strip once the pane slides away
                var close = new DoubleAnimation(target, 0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                };
                close.Completed += (_, _) =>
                {
                    if (ViewModel.SelectedTask != null) return;   // re-opened mid-slide
                    DetailPane.Width = 0;
                    DetailPane.Visibility = Visibility.Collapsed;
                    DetailPane.DataContext = null;
                };
                DetailPane.BeginAnimation(FrameworkElement.WidthProperty, close);
            }
            return;
        }

        var wasCollapsed = DetailPane.Visibility == Visibility.Collapsed;
        DetailPane.DataContext = task;                       // snapshot before opening
        DetailPane.Visibility = Visibility.Visible;
        DetailSplitter.Visibility = Visibility.Visible;
        if (wasCollapsed)
        {
            var open = new DoubleAnimation(0, target, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            DetailPane.BeginAnimation(FrameworkElement.WidthProperty, open);
        }
        else
        {
            // Pane already visible — task→task switch just repoints DataContext. If it was
            // mid-close (re-opened during slide-out), cancel the slide and snap back to full
            // width instead of finishing the collapse (Completed's SelectedTask guard would
            // leave the pane stuck at width 0 but visible). Set the local value first so
            // clearing the animation reverts to the saved width, not the XAML base of 0.
            DetailPane.Width = target;
            DetailPane.BeginAnimation(FrameworkElement.WidthProperty, null);
        }
        RefreshDetailPickers();
    }

    /// <summary>The splitter is the pane's LEFT edge: dragging left (−delta) widens the pane,
    /// right narrows it. Capture + drag-cancel so it works mid slide-open.</summary>
    private void DetailSplitter_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _detailResizing = true;
        _detailResizeStartX = e.GetPosition(this).X;
        _detailResizeStartWidth = DetailPane.Width;
        DetailPane.BeginAnimation(FrameworkElement.WidthProperty, null);   // cancel any open/close slide
        DetailSplitter.CaptureMouse();
        e.Handled = true;
    }

    private void DetailSplitter_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_detailResizing) return;
        var delta = e.GetPosition(this).X - _detailResizeStartX;
        DetailPane.Width = Math.Clamp(_detailResizeStartWidth - delta, DetailPaneMinWidth, DetailPaneMaxWidth);
    }

    private void DetailSplitter_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_detailResizing) return;
        _detailResizing = false;
        DetailSplitter.ReleaseMouseCapture();
        SettingsService.Current.DetailPaneWidth = DetailPane.Width;
        SettingsService.Save();
    }

    private void DetailPane_Close(object sender, RoutedEventArgs e)
    {
        ViewModel.SelectedTask = null;
    }

    private void DetailPane_EditCloseTime(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask != null)
            OpenEditCloseTimeDialog(ViewModel.SelectedTask);
    }

    // ─── Detail Pane: persist title / note edits ─────────
    private string _detailFieldOriginal = "";

    private void DetailField_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            _detailFieldOriginal = tb.Text;
    }

    private void DetailField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            // Commit the pending binding update first so the edited value reaches
            // SelectedTask regardless of handler/binding order, then persist.
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (ViewModel.SelectedTask != null && tb.Text != _detailFieldOriginal)
                ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        }
    }

    private void DetailPane_Delete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        if (FluentDialog.Confirm(this, Loc.ConfirmDeleteMsg(ViewModel.SelectedTask.Title), Loc.ConfirmDelete))
        {
            ViewModel.DeleteTaskCommand.Execute(ViewModel.SelectedTask);
        }
    }

    // ─── Detail Pane: Tags & Group ────────────────────────
    public void RefreshDetailPickers()
    {
        var task = ViewModel.SelectedTask;
        if (task == null) return;

        _suppressDetailEvents = true;

        // Recurrence label
        RecurrenceLabel.Text = task.Recurrence == RecurrenceFrequency.None
            ? Loc.AddRecurrence
            : Loc.RecurrenceName(task.Recurrence);

        // Reminder label
        if (task.Reminder != null)
        {
            var rdt = DateTimeOffset.FromUnixTimeMilliseconds(task.Reminder.Value).LocalDateTime;
            ReminderLabel.Text = Loc.ReminderTime(rdt);
        }
        else
        {
            ReminderLabel.Text = Loc.AddReminder;
        }

        // Due date label
        if (task.DueDate != null)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(task.DueDate.Value).LocalDateTime;
            DueDateLabel.Text = dt.Date == DateTime.Today ? Loc.Today
                : dt.Date == DateTime.Today.AddDays(1) ? Loc.Tomorrow
                : Loc.ShortDate(dt);
        }
        else
        {
            DueDateLabel.Text = Loc.AddDueDate;
        }

        // Populate tag list
        var taskTags = ViewModel.Tags.Where(t => task.TagIds.Contains(t.Id)).ToList();
        DetailTagList.ItemsSource = taskTags;

        // Populate group combo
        DetailGroupBox.Items.Clear();
        var allGroups = ViewModel.Groups.Where(g => g.ListId == task.ListId).ToList();
        DetailGroupBox.Items.Add(Loc.Ungrouped);
        foreach (var g in allGroups)
            DetailGroupBox.Items.Add(g);
        DetailGroupBox.SelectedIndex = 0;
        if (task.GroupId != null)
        {
            for (int i = 0; i < DetailGroupBox.Items.Count; i++)
            {
                if (DetailGroupBox.Items[i] is TaskGroup g && g.Id == task.GroupId)
                {
                    DetailGroupBox.SelectedIndex = i;
                    break;
                }
            }
        }

        _suppressDetailEvents = false;
    }

    private void DetailAddTag_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not Button btn) return;

        var available = ViewModel.Tags.Where(t => !ViewModel.SelectedTask.TagIds.Contains(t.Id)).ToList();
        if (available.Count == 0) return;

        var menu = new ContextMenu();
        foreach (var tag in available)
        {
            var item = new MenuItem { Header = tag.Name };
            var captured = tag;
            item.Click += (s, _) =>
            {
                ViewModel.AddTagToTaskCommand.Execute((ViewModel.SelectedTask, captured));
                RefreshDetailPickers();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = btn;
        menu.IsOpen = true;
    }

    private void DetailTag_Remove(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is Tag tag && ViewModel.SelectedTask != null)
        {
            ViewModel.RemoveTagFromTaskCommand.Execute((ViewModel.SelectedTask, tag));
            RefreshDetailPickers();
        }
    }

    private void DetailGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressDetailEvents) return;
        if (ViewModel.SelectedTask == null) return;
        if (DetailGroupBox.SelectedItem is TaskGroup g)
        {
            ViewModel.MoveTaskToGroupCommand.Execute((ViewModel.SelectedTask, g));
            RefreshDetailPickers();
        }
        else if (DetailGroupBox.SelectedIndex == 0)
        {
            ViewModel.MoveTaskToGroupCommand.Execute((ViewModel.SelectedTask, null));
            RefreshDetailPickers();
        }
    }

    private void Star_Click(object sender, MouseButtonEventArgs e)
    {
        // Walk up to find the task from the TaskRowReorder border
        DependencyObject? current = sender as DependencyObject;
        while (current != null && (current as FrameworkElement)?.DataContext is not TaskItem)
            current = VisualTreeHelper.GetParent(current);
        if (current is FrameworkElement fe && fe.DataContext is TaskItem task)
        {
            ViewModel.ToggleImportantCommand.Execute(task);
        }
        e.Handled = true;
    }
}
