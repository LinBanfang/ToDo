using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo.Views;

public partial class DetailPaneControl : UserControl
{
    private MainViewModel ViewModel => DataContext as MainViewModel ?? App.ViewModel!;

    // ─── Detail Pane ──────────────────────────────────────

    private const double DetailPaneMinWidth = 240;
    private const double DetailPaneMaxWidth = 560;

    private bool _detailResizing;
    private double _detailResizeStartWidth;
    private double _detailResizeStartX;

    // Step-handle drag (fields can't be shared across controls — each drag site owns its own).
    private Point _dragStartPoint;

    public DetailPaneControl()
    {
        InitializeComponent();
    }

    /// <summary>Opens/closes the detail pane with a width slide so the themed task area
    /// re-crops smoothly instead of snapping (the horizontal shift when the column
    /// appears/disappears). The slide targets the persisted width (SettingsService.
    /// DetailPaneWidth), which the pane's splitter can also resize directly. Content binds to
    /// the pane's DataContext — a snapshot of the task — so it stays rendered during slide-out
    /// instead of going blank.</summary>
    public void UpdateForSelectedTask()
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

    private void OpenEditCloseTimeDialog(TaskItem task)
    {
        if (task.CloseRecord == null) return;
        var dialog = new DateTimeDialog(task.CloseRecord.ClosedAt) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Saved)
            ViewModel.EditCloseTimeCommand.Execute((task, dialog.ResultTimestamp));
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
        if (FluentDialog.Confirm(Window.GetWindow(this), Loc.ConfirmDeleteMsg(ViewModel.SelectedTask.Title), Loc.ConfirmDelete))
        {
            ViewModel.DeleteTaskCommand.Execute(ViewModel.SelectedTask);
        }
    }

    // ─── Detail Pane: Tags & Group ────────────────────────
    private bool _suppressDetailEvents;

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

    // ─── Step-handle drag ─────────────────────────────────
    private void RecordDragStart(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            _dragStartPoint = e.GetPosition(null);
    }

    private bool DragThresholdExceeded(MouseEventArgs e)
    {
        var pos = e.GetPosition(null);
        return Math.Abs(pos.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(pos.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }

    // ─── Attachments (local-only, ADR-013) ──────────────────
    private const int MaxAttachmentMb = 50;
    private const long MaxAttachmentBytes = MaxAttachmentMb * 1024 * 1024L;

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        var task = ViewModel.SelectedTask;
        if (task == null) return;

        var dlg = new OpenFileDialog { Title = Loc.AddAttachment, Multiselect = true };
        if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

        foreach (var file in dlg.FileNames)
            AddAttachmentFile(task, file);
        ReloadDetailAttachments();
    }

    private void AttachmentPanel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void AttachmentPanel_Drop(object sender, DragEventArgs e)
    {
        var task = ViewModel.SelectedTask;
        if (task == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        foreach (var f in files)
            AddAttachmentFile(task, f);
        ReloadDetailAttachments();
    }

    private void AddAttachmentFile(TaskItem task, string filePath)
    {
        FileInfo? info = null;
        try
        {
            info = new FileInfo(filePath);
            if (info.Length > MaxAttachmentBytes)
            {
                FluentDialog.Show(Window.GetWindow(this), Loc.AttachmentTooLarge(MaxAttachmentMb), Loc.Error);
                return;
            }
            App.Database!.AddAttachment(new TaskAttachment
            {
                TaskId = task.Id,
                FileName = info.Name,
                Size = info.Length,
                Data = File.ReadAllBytes(filePath),
                AddedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        }
        catch
        {
            FluentDialog.Show(Window.GetWindow(this), Loc.AttachmentOpenFailed(info?.Name ?? filePath), Loc.Error);
        }
    }

    private void AttachmentOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskAttachment att)
        {
            try
            {
                // Extract to a unique temp file (Id prefix avoids name collisions),
                // then let the OS pick the default handler by extension.
                var dir = Path.Combine(Path.GetTempPath(), "ToDoAttachments");
                Directory.CreateDirectory(dir);
                var invalid = Path.GetInvalidFileNameChars();
                var name = new string($"{att.Id}-{att.FileName}".Select(c => invalid.Contains(c) ? '_' : c).ToArray());
                var path = Path.Combine(dir, name);
                File.WriteAllBytes(path, att.Data);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                FluentDialog.Show(Window.GetWindow(this), Loc.AttachmentOpenFailed(att.FileName), Loc.Error);
            }
        }
    }

    private void AttachmentRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not FrameworkElement fe || fe.DataContext is not TaskAttachment att) return;
        App.Database!.DeleteAttachment(att.Id);
        ReloadDetailAttachments();
    }

    /// <summary>Re-reads the selected task's attachments from the DB into the [BsonIgnore]
    /// list the detail pane binds to, and refreshes the row paperclip count.</summary>
    private void ReloadDetailAttachments()
    {
        var task = ViewModel.SelectedTask;
        if (task == null) return;
        task.Attachments.Clear();
        foreach (var a in App.Database!.GetAttachments(task.Id))
            task.Attachments.Add(a);
        App.Database.RefreshAttachmentCounts(new[] { task });
    }

    private void DueDateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not Button btn) return;

        var today = DateTime.Today;
        var menu = new ContextMenu { PlacementTarget = btn };

        var todayItem = new MenuItem { Header = Loc.Today };
        todayItem.Click += (s, _) => SetDueDate(today);
        menu.Items.Add(todayItem);

        var tomorrowItem = new MenuItem { Header = Loc.Tomorrow };
        tomorrowItem.Click += (s, _) => SetDueDate(today.AddDays(1));
        menu.Items.Add(tomorrowItem);

        // Next week (next Monday)
        var nextMonday = GetNextMonday();
        var nextWeekItem = new MenuItem { Header = Loc.ThisWeek };
        nextWeekItem.Click += (s, _) => SetDueDate(nextMonday);
        menu.Items.Add(nextWeekItem);

        menu.Items.Add(new Separator());

        var pickItem = new MenuItem { Header = Loc.PickDate };
        pickItem.Click += (s, _) =>
        {
            var dialog = new DateTimeDialog(
                ViewModel.SelectedTask!.DueDate ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            { Owner = Window.GetWindow(this), Title = Loc.Date };
            if (dialog.ShowDialog() == true && dialog.Saved)
            {
                SetDueDate(DateTimeOffset.FromUnixTimeMilliseconds(dialog.ResultTimestamp).LocalDateTime.Date);
            }
        };
        menu.Items.Add(pickItem);

        if (ViewModel.SelectedTask.DueDate != null)
        {
            menu.Items.Add(new Separator());
            var removeItem = new MenuItem { Header = $"✕  {Loc.Delete}" };
            removeItem.Click += (s, _) =>
            {
                ViewModel.SelectedTask!.DueDate = null;
                ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
                RefreshDetailPickers();
            };
            menu.Items.Add(removeItem);
        }

        menu.IsOpen = true;
    }

    private void SetDueDate(DateTime date)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.DueDate = new DateTimeOffset(date).ToUnixTimeMilliseconds();
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

    private void ReminderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not Button btn) return;

        var menu = new ContextMenu { PlacementTarget = btn };
        var now = DateTime.Now;

        foreach (var (label, offset) in new[] {
            (Loc.HoursFromNow(1), 1.0),
            (Loc.HoursFromNow(3), 3.0),
            (Loc.Tomorrow + " 9:00", (now.Date.AddDays(1).AddHours(9) - now).TotalHours),
            (Loc.ThisWeek + " 9:00", (GetNextMonday().AddHours(9) - now).TotalHours),
        })
        {
            var item = new MenuItem { Header = label };
            var ts = DateTimeOffset.UtcNow.AddHours(offset).ToUnixTimeMilliseconds();
            item.Click += (_, _) => SetReminder(ts);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());

        var pickItem = new MenuItem { Header = Loc.PickDate };
        pickItem.Click += (_, _) =>
        {
            // includeTime: reminders carry a time of day (due dates intentionally do not).
            var dlg = new DateTimeDialog(
                ViewModel.SelectedTask!.Reminder ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                includeTime: true)
            { Owner = Window.GetWindow(this), Title = Loc.Reminder };
            if (dlg.ShowDialog() == true && dlg.Saved)
                SetReminder(dlg.ResultTimestamp);
        };
        menu.Items.Add(pickItem);

        if (ViewModel.SelectedTask.Reminder != null)
        {
            menu.Items.Add(new Separator());
            var removeItem = new MenuItem { Header = $"✕  {Loc.Delete}" };
            removeItem.Click += (_, _) => ReminderClear_Click(sender, e);
            menu.Items.Add(removeItem);
        }
        menu.IsOpen = true;
    }

    private static DateTime GetNextMonday()
    {
        var today = DateTime.Today;
        var daysUntil = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7;
        return today.AddDays(daysUntil);
    }

    private void SetReminder(long ts)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.Reminder = ts;
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

    private void ReminderClear_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.Reminder = null;
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

    private void RecurrenceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not Button btn) return;

        var menu = new ContextMenu { PlacementTarget = btn };
        foreach (var (freq, header) in new[] {
            (RecurrenceFrequency.None, Loc.RepeatNone),
            (RecurrenceFrequency.Daily, Loc.RepeatDaily),
            (RecurrenceFrequency.Weekdays, Loc.RepeatWeekdays),
            (RecurrenceFrequency.Weekly, Loc.RepeatWeekly),
            (RecurrenceFrequency.Monthly, Loc.RepeatMonthly),
            (RecurrenceFrequency.Yearly, Loc.RepeatYearly),
        })
        {
            var item = new MenuItem { Header = header };
            var f = freq;
            item.Click += (_, _) => SetRecurrence(f);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void SetRecurrence(RecurrenceFrequency freq)
    {
        if (ViewModel.SelectedTask is not { } task) return;
        task.Recurrence = freq;
        // Recurring tasks need a due date to schedule the next instance (ADR-015):
        // picking a rule without one backdates it to today, so generation has an anchor.
        if (freq != RecurrenceFrequency.None && task.DueDate == null)
            task.DueDate = new DateTimeOffset(DateTime.Today).ToUnixTimeMilliseconds();
        ViewModel.UpdateTaskCommand.Execute(task);
        RefreshDetailPickers();
    }

    private void DetailDueDate_Clear(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.DueDate = null;
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

    // ─── Steps ────────────────────────────────────────────
    private void AddStepBox_ButtonClick(object sender, RoutedEventArgs e)
    {
        AddStepBox.Focus();
    }

    private void AddStepBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text)
            && ViewModel.SelectedTask != null)
        {
            ViewModel.AddStepCommand.Execute((ViewModel.SelectedTask, tb.Text.Trim()));
            tb.Text = "";
            e.Handled = true;
        }
    }

    private void MyDayToggle_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask != null)
            ViewModel.ToggleMyDayCommand.Execute(ViewModel.SelectedTask);
    }

    private void StepToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask != null)
            ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
    }

    private void StepTitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskStep step)
        {
            step.EditTitle = step.Title;
            step.IsEditing = true;
        }
    }

    private void StepEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskStep step)
        {
            if (e.Key == Key.Enter)
            {
                CommitStepEdit(step);
                if (ViewModel.SelectedTask != null)
                    ViewModel.InsertStepAfter(ViewModel.SelectedTask, step.Order);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape) { step.IsEditing = false; e.Handled = true; }
        }
    }

    private void StepEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskStep step)
            CommitStepEdit(step);
    }

    private void CommitStepEdit(TaskStep step)
    {
        var n = step.EditTitle?.Trim();
        if (!string.IsNullOrEmpty(n) && n != step.Title)
        {
            step.Title = n;
            if (ViewModel.SelectedTask != null)
                ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        }
        step.IsEditing = false;
    }

    // ─── Step handle: drag to reorder, click for menu ────
    private void StepHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe
            && fe.DataContext is TaskStep step && DragThresholdExceeded(e))
            DragDrop.DoDragDrop(fe, step, DragDropEffects.Move);
    }

    private void StepHandle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not TaskStep step
            || ViewModel.SelectedTask == null) return;

        var menu = new ContextMenu { PlacementTarget = fe as UIElement };
        var completeItem = new MenuItem { Header = step.Completed ? Loc.MarkIncomplete : Loc.Complete };
        completeItem.Click += (_, _) => { step.Completed = !step.Completed; ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask); };
        menu.Items.Add(completeItem);
        menu.Items.Add(new Separator());
        var promoteItem = new MenuItem { Header = Loc.PromoteToTask };
        promoteItem.Click += (_, _) =>
        {
            if (ViewModel.SelectedTask != null)
                ViewModel.PromoteStepToTaskCommand.Execute((ViewModel.SelectedTask, step));
        };
        menu.Items.Add(promoteItem);
        menu.Items.Add(new Separator());
        var deleteItem = new MenuItem { Header = Loc.Delete };
        deleteItem.Click += (_, _) => ViewModel.DeleteStepCommand.Execute((ViewModel.SelectedTask!, step));
        menu.Items.Add(deleteItem);
        menu.IsOpen = true;
    }

    private Border? _lastStepDropRow;

    private void StepRow_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskStep)) && sender is Border border)
        {
            e.Effects = DragDropEffects.Move;
            UpdateStepRowDropIndicator(border, e);
        }
        e.Handled = true;
    }

    private void StepRow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskStep)) && sender is Border border)
        {
            e.Effects = DragDropEffects.Move;
            UpdateStepRowDropIndicator(border, e);
        }
        e.Handled = true;
    }

    private void StepRow_DragLeave(object sender, DragEventArgs e)
    {
        ClearStepRowDropIndicator();
        e.Handled = true;
    }

    private void UpdateStepRowDropIndicator(Border border, DragEventArgs e)
    {
        ClearStepRowDropIndicator();
        bool lowerHalf = e.GetPosition(border).Y > border.ActualHeight / 2;
        border.BorderBrush = (Brush)Application.Current.FindResource("AccentBlue");
        border.BorderThickness = new Thickness(0, lowerHalf ? 0 : 2, 0, lowerHalf ? 2 : 0);
        _lastStepDropRow = border;
    }

    private void ClearStepRowDropIndicator()
    {
        if (_lastStepDropRow != null)
        {
            _lastStepDropRow.BorderBrush = Brushes.Transparent;
            _lastStepDropRow.BorderThickness = new Thickness(0);
            _lastStepDropRow = null;
        }
    }

    private void StepRow_Drop(object sender, DragEventArgs e)
    {
        ClearStepRowDropIndicator();
        if (sender is Border border && border.DataContext is TaskStep target
            && ViewModel.SelectedTask != null
            && e.Data.GetDataPresent(typeof(TaskStep))
            && e.Data.GetData(typeof(TaskStep)) is TaskStep dragged && dragged.Id != target.Id)
        {
            var steps = ViewModel.SelectedTask.Steps;
            // Upper half of the target row inserts before it, lower half after it
            bool lowerHalf = e.GetPosition(border).Y > border.ActualHeight / 2;
            if (!ReorderService.Reorder(steps, dragged, target, lowerHalf))
            {
                e.Handled = true;
                return;
            }
            ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        }
        e.Handled = true;
    }

    /// <summary>Flushes pending title/note edits on window close so nothing typed but not
    /// yet committed is lost (a tray-close hides the window without a LostFocus).</summary>
    public void FlushPendingEdits()
    {
        DetailTitleBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        DetailNoteBox?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (ViewModel.SelectedTask != null)
            ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
    }
}
