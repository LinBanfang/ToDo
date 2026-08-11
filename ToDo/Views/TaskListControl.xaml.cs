using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo.Views;

public partial class TaskListControl : UserControl
{
    private MainViewModel ViewModel => DataContext as MainViewModel ?? App.ViewModel!;

    public TaskListControl()
    {
        InitializeComponent();
        UpdateAddTaskPlaceholder();
    }

    /// <summary>Focus for Ctrl+N (the task area is a separate control now, so the window
    /// can't reach AddTaskBox by name).</summary>
    public void FocusNewTaskBox() => AddTaskBox.Focus();

    // ─── Add Task Box ─────────────────────────────────────
    private void AddTaskBox_GotFocus(object sender, RoutedEventArgs e)
    {
        // Focus switches the placeholder into a task row: "+" becomes the empty checkbox.
        AddTaskCheckbox.Visibility = Visibility.Visible;
        AddTaskPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void AddTaskBox_LostFocus(object sender, RoutedEventArgs e)
    {
        AddTaskCheckbox.Visibility = Visibility.Collapsed;
        UpdateAddTaskPlaceholder();
    }

    private void AddTaskBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateAddTaskPlaceholder();
    }

    private void UpdateAddTaskPlaceholder()
    {
        // Placeholder ("+ 添加任务") only while the box is empty and not focused.
        AddTaskPlaceholder.Visibility =
            !AddTaskBox.IsKeyboardFocused && string.IsNullOrEmpty(AddTaskBox.Text)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            ViewModel.CreateTaskCommand.Execute(tb.Text.Trim());
            tb.Text = "";
            e.Handled = true;
        }
    }

    // When the task-area scrollbar appears it shrinks the task rows' width, so the
    // fixed add-task footer would otherwise stick out ~scrollbar-width on the right.
    // Mirror the scrollbar into the footer's right padding to keep both right edges
    // aligned. Fires on scrollbar show/hide, content changes and window resize.
    private void TaskAreaScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        double extra = TaskAreaScroll.ComputedVerticalScrollBarVisibility == Visibility.Visible
            ? SystemParameters.VerticalScrollBarWidth
            : 0;
        AddTaskFooter.Padding = new Thickness(32, 8, 32 + extra, 12);
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateGroupCommand.Execute(Loc.NewGroup);
    }

    // ─── Group headers ────────────────────────────────────
    private bool _suppressGroupHeaderToggle;

    private void GroupHeader_Toggle(object sender, MouseButtonEventArgs e)
    {
        // Consume suppression set when a group drag or a drop onto the header ends,
        // so the mouse-up that follows doesn't collapse/expand the group.
        if (_suppressGroupHeaderToggle) { _suppressGroupHeaderToggle = false; return; }
        if (e.ClickCount >= 2) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not GroupedTasks gt) return;
        // The name is double-click-to-rename; toggling here would rebuild the list and
        // reset ClickCount, breaking the rename. Also stay out of the way while editing.
        if (gt.IsEditing || IsInsideTaggedElement(e.OriginalSource, "GroupName")) return;
        ViewModel.ToggleGroupCollapseCommand.Execute(gt.Group);
    }

    private void GroupHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe
            && fe.DataContext is GroupedTasks gt && gt.HasGroup && !gt.IsEditing
            && DragThresholdExceeded(e))
        {
            SetGroupHeaderToggleSuppressed();
            DragDrop.DoDragDrop(fe, gt.Group!, DragDropEffects.Move);
        }
    }

    private void SetGroupHeaderToggleSuppressed()
    {
        _suppressGroupHeaderToggle = true;
        // Safety net: clear after input processing in case no mouse-up hits the header
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _suppressGroupHeaderToggle = false);
    }

    private void GroupHeader_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is Border border && border.DataContext is GroupedTasks gt)
        {
            var menu = border.ContextMenu;
            if (menu == null) return;
            menu.Items.Clear();

            var renameItem = new MenuItem { Header = Loc.Rename };
            renameItem.Click += (s, _) =>
            {
                gt.EditName = gt.Group!.Name;
                gt.IsEditing = true;
            };
            menu.Items.Add(renameItem);

            menu.Items.Add(new Separator());

            var deleteGItem = new MenuItem { Header = Loc.DeleteGroup };
            deleteGItem.Click += (s, _) =>
            {
                if (FluentDialog.Confirm(Window.GetWindow(this), Loc.ConfirmDeleteGroupMsg(gt.Group!.Name), Loc.DeleteGroup))
                    ViewModel.DeleteGroupCommand.Execute(gt.Group);
            };
            menu.Items.Add(deleteGItem);
        }
    }

    private void GroupName_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is FrameworkElement fe && fe.DataContext is GroupedTasks gt && gt.Group != null)
        {
            gt.EditName = gt.Group.Name;
            gt.IsEditing = true;
            e.Handled = true;
        }
    }

    private void GroupNameEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GroupedTasks gt)
        {
            if (e.Key == Key.Enter)
            {
                CommitGroupRename(gt);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                gt.IsEditing = false;
                e.Handled = true;
            }
        }
    }

    private void GroupNameEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GroupedTasks gt)
        {
            CommitGroupRename(gt);
        }
    }

    private void CommitGroupRename(GroupedTasks gt)
    {
        if (gt.Group == null) return;
        var newName = gt.EditName?.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != gt.Group.Name)
        {
            gt.Group.Name = newName;
            ViewModel.RenameGroupCommand.Execute(gt.Group);
        }
        gt.IsEditing = false;
    }

    // ─── Task Row Events ──────────────────────────────────
    private bool _suppressTaskClick;

    private void SuppressPendingTaskClick()
    {
        _suppressTaskClick = true;
        // Clear after the pending mouse-up/click events have been dispatched
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => _suppressTaskClick = false);
    }

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (_suppressTaskClick) return;
        if (sender is Border border && border.DataContext is TaskItem task)
        {
            ViewModel.SelectedTask = task;
        }
    }

    // ─── Inline task title rename ──────────────────────────
    private void TaskTitle_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is FrameworkElement fe && fe.DataContext is TaskItem task && !task.IsClosed)
        {
            task.EditTitle = task.Title;
            task.IsEditingTitle = true;
            e.Handled = true;
        }
    }

    private void TaskTitleEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox tb && tb.IsVisible)
            Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); });
    }

    private void TaskTitleEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskItem task)
        {
            if (e.Key == Key.Enter) { CommitTaskTitle(task); e.Handled = true; }
            else if (e.Key == Key.Escape) { task.IsEditingTitle = false; e.Handled = true; }
        }
    }

    private void TaskTitleEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskItem task)
            CommitTaskTitle(task);
    }

    private void CommitTaskTitle(TaskItem task)
    {
        var n = task.EditTitle?.Trim();
        task.IsEditingTitle = false;
        if (!string.IsNullOrEmpty(n) && n != task.Title)
        {
            task.Title = n;
            ViewModel.UpdateTaskCommand.Execute(task);
        }
    }

    private void Checkbox_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskItem task)
        {
            ViewModel.CloseTaskCommand.Execute((task, CloseMode.Complete, false));
            e.Handled = true;
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

    private void TaskRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is Border border && border.DataContext is TaskItem task)
        {
            var menu = border.ContextMenu;
            if (menu == null) return;

            menu.Items.Clear();

            var isClosed = task.CloseRecord != null;

            if (!isClosed)
            {
                var completeItem = new MenuItem { Header = Loc.Complete };
                completeItem.Click += (s, _) =>
                    ViewModel.CloseTaskCommand.Execute((task, CloseMode.Complete, false));
                menu.Items.Add(completeItem);

                if (task.Recurrence != RecurrenceFrequency.None)
                {
                    // Recurring task: "cancel" splits into skip-this-occurrence (series
                    // continues) vs stop-repeating (series ends), per ADR-015.
                    var skipItem = new MenuItem { Header = Loc.SkipOccurrence };
                    skipItem.Click += (s, _) =>
                        ViewModel.CloseTaskCommand.Execute((task, CloseMode.Cancel, false));
                    menu.Items.Add(skipItem);

                    var endSeriesItem = new MenuItem { Header = Loc.EndSeries };
                    endSeriesItem.Click += (s, _) =>
                        ViewModel.CloseTaskCommand.Execute((task, CloseMode.Cancel, true));
                    menu.Items.Add(endSeriesItem);
                }
                else
                {
                    var cancelItem = new MenuItem { Header = Loc.Cancel };
                    cancelItem.Click += (s, _) =>
                        ViewModel.CloseTaskCommand.Execute((task, CloseMode.Cancel, false));
                    menu.Items.Add(cancelItem);
                }

                menu.Items.Add(new Separator());

                var myDayItem = new MenuItem
                {
                    Header = task.IsMyDay ? Loc.RemoveFromMyDay : Loc.AddToMyDay
                };
                myDayItem.Click += (s, _) => ViewModel.ToggleMyDayCommand.Execute(task);
                menu.Items.Add(myDayItem);

                var impItem = new MenuItem
                {
                    Header = task.IsImportant ? Loc.RemoveImportance : Loc.MarkImportant
                };
                impItem.Click += (s, _) => ViewModel.ToggleImportantCommand.Execute(task);
                menu.Items.Add(impItem);

                menu.Items.Add(new Separator());

                // Move to list submenu
                var customLists = ViewModel.Lists
                    .Where(l => l.Type == ListType.Custom && l.Id != task.ListId)
                    .ToList();
                if (customLists.Count > 0)
                {
                    var moveMenu = new MenuItem { Header = Loc.MoveToList };
                    foreach (var list in customLists)
                    {
                        var listItem = new MenuItem { Header = list.Name };
                        var capturedList = list;
                        listItem.Click += (s, _) =>
                            ViewModel.MoveTaskToListCommand.Execute((task, capturedList));
                        moveMenu.Items.Add(listItem);
                    }
                    menu.Items.Add(moveMenu);
                }

                // Move to group submenu (only for custom lists)
                if (ViewModel.ActiveList?.Type == ListType.Custom)
                {
                    var groupsInList = ViewModel.Groups
                        .Where(g => g.ListId == ViewModel.ActiveList.Id)
                        .ToList();
                    var moveGroupMenu = new MenuItem { Header = Loc.MoveToGroup };
                    if (task.GroupId != null)
                    {
                        var ungroupItem = new MenuItem { Header = Loc.RemoveFromGroup };
                        ungroupItem.Click += (s, _) =>
                            ViewModel.MoveTaskToGroupCommand.Execute((task, null));
                        moveGroupMenu.Items.Add(ungroupItem);
                        moveGroupMenu.Items.Add(new Separator());
                    }
                    foreach (var g in groupsInList)
                    {
                        if (g.Id == task.GroupId) continue; // skip current group
                        var groupItem = new MenuItem { Header = g.Name };
                        var capturedGroup = g;
                        groupItem.Click += (s, _) =>
                            ViewModel.MoveTaskToGroupCommand.Execute((task, capturedGroup));
                        moveGroupMenu.Items.Add(groupItem);
                    }
                    if (moveGroupMenu.Items.Count > 0)
                        menu.Items.Add(moveGroupMenu);
                }

                // Add tag submenu
                var tagMenu = new MenuItem { Header = Loc.Tags };
                foreach (var tag in ViewModel.Tags)
                {
                    var assigned = task.TagIds.Contains(tag.Id);
                    var tagItem = new MenuItem
                    {
                        Header = $"{(assigned ? "✓ " : "")}{tag.Name}"
                    };
                    var capturedTag = tag;
                    tagItem.Click += (s, _) =>
                    {
                        if (task.TagIds.Contains(capturedTag.Id))
                            ViewModel.RemoveTagFromTaskCommand.Execute((task, capturedTag));
                        else
                            ViewModel.AddTagToTaskCommand.Execute((task, capturedTag));
                    };
                    tagMenu.Items.Add(tagItem);
                }
                if (ViewModel.Tags.Count == 0)
                    tagMenu.Items.Add(new MenuItem { Header = Loc.NoTags, IsEnabled = false });
                menu.Items.Add(tagMenu);
            }
            else
            {
                var reopenItem = new MenuItem { Header = Loc.ReopenTask };
                reopenItem.Click += (s, _) => ViewModel.ReopenTaskCommand.Execute(task);
                menu.Items.Add(reopenItem);

                var editTimeItem = new MenuItem { Header = Loc.EditCloseTime };
                editTimeItem.Click += (s, _) => OpenEditCloseTimeDialog(task);
                menu.Items.Add(editTimeItem);
            }

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = Loc.DeleteTask };
            deleteItem.Click += (s, _) =>
            {
                if (FluentDialog.Confirm(Window.GetWindow(this), Loc.ConfirmDeleteMsg(task.Title), Loc.ConfirmDelete))
                    ViewModel.DeleteTaskCommand.Execute(task);
            };
            menu.Items.Add(deleteItem);
        }
    }

    private void OpenEditCloseTimeDialog(TaskItem task)
    {
        if (task.CloseRecord == null) return;
        var dialog = new DateTimeDialog(task.CloseRecord.ClosedAt) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Saved)
            ViewModel.EditCloseTimeCommand.Execute((task, dialog.ResultTimestamp));
    }

    // ─── Task-area drag & drop ────────────────────────────
    private Point _dragStartPoint;

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

    /// <summary>True if the event source lies inside an element tagged with the given value.</summary>
    private static bool IsInsideTaggedElement(object? source, string tag)
    {
        var el = source as DependencyObject;
        while (el != null)
        {
            if (el is FrameworkElement fe && fe.Tag is string s && s == tag) return true;
            el = VisualTreeHelper.GetParent(el);
        }
        return false;
    }

    private void TaskRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe
            && fe.DataContext is TaskItem task && !task.IsClosed && !task.IsEditingTitle
            && DragThresholdExceeded(e))
        {
            // While the drag runs the pointer is tracked so the empty-ungrouped drop
            // slot only appears when the drag reaches the top zone (near the first
            // group's header) — see TaskArea_PreviewDragOver. Both flags reset when the
            // drag finishes (drop, cancel or Esc). DoDragDrop blocks, so the flags are
            // scoped correctly even though the handler returns below them.
            ViewModel.IsTaskDragging = true;
            _ungroupedZoneBottom = FindUngroupedZoneBottom();
            try
            {
                DragDrop.DoDragDrop(fe, task, DragDropEffects.Move);
            }
            finally
            {
                ViewModel.IsTaskDragging = false;
                _ungroupedZoneBottom = null;
                SetDropSlotOpen(false);
            }
        }
    }

    /// <summary>Expanded height of the empty-ungrouped drop slot (matches the XAML
    /// Padding so the hint text fits).</summary>
    private const double UngroupedSlotHeight = 36;

    private static readonly TimeSpan SlotOpenDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan SlotCloseDuration = TimeSpan.FromMilliseconds(120);

    /// <summary>Bottom edge (content Y) of the "ungrouped zone" for the current drag:
    /// the first group header's mid, pushed down by the slot's expanded height so the
    /// whole open slot stays inside the zone. Null when there's no first group (a slot
    /// can't be shown without one). Set at drag start, cleared on drag end.</summary>
    private double? _ungroupedZoneBottom;

    /// <summary>Current open/closed state of the drop slot, so the high-frequency drag
    /// events don't restart the animations every frame.</summary>
    private bool _dropSlotOpen;

    /// <summary>Tracks the pointer while a task row is dragged so the drop slot only
    /// opens in the top zone. Attached to the task-area ScrollViewer: over the section
    /// drop targets the tunneling event still reaches it (sections set e.Handled on the
    /// bubbling DragOver), and its own AllowDrop covers the strips between sections.
    /// No DragLeave handling on purpose — a leave fires whenever the target moves to a
    /// neighbouring section, which would flicker the slot at the top-zone boundary.</summary>
    private void TaskArea_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsTaskDragging || !e.Data.GetDataPresent(typeof(TaskItem))) return;
        var pos = e.GetPosition(TaskAreaPanel);
        SetDropSlotOpen(_ungroupedZoneBottom != null && pos.Y <= _ungroupedZoneBottom.Value);
    }

    /// <summary>Slides the empty-ungrouped drop slot in/out. Driven directly rather than
    /// via a Style trigger's EnterActions storyboard, which WPF does not revert when the
    /// trigger deactivates — the slot would stay stuck open after the pointer leaves the
    /// top zone or the drag is released.</summary>
    private void SetDropSlotOpen(bool open)
    {
        if (_dropSlotOpen == open) return;
        _dropSlotOpen = open;
        var slot = FindDropSlot();
        if (slot == null) return;
        if (open)
        {
            slot.BeginAnimation(FrameworkElement.HeightProperty,
                new DoubleAnimation(UngroupedSlotHeight, SlotOpenDuration)
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            slot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, SlotOpenDuration));
            slot.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(new Thickness(32, 2, 32, 2), SlotOpenDuration));
        }
        else
        {
            slot.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(0, SlotCloseDuration));
            slot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, SlotCloseDuration));
            slot.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(new Thickness(32, 0, 32, 0), SlotCloseDuration));
        }
    }

    /// <summary>Locates the drop slot inside the (first = ungrouped) section's container.
    /// Null when the group list is hidden, e.g. while viewing a system list.</summary>
    private Border? FindDropSlot()
    {
        var container = GroupedTasksControl.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
        if (container == null) return null;
        return FindVisualChild<Border>(container, b => b.Tag is string s && s == "UngroupDropSlot");
    }

    /// <summary>Content-Y of the boundary below which the "ungrouped zone" ends. The
    /// zone covers the drop slot plus the first group's upper half: the header's mid,
    /// shifted down by the slot's expanded height so the whole open slot stays inside
    /// the zone (otherwise dragging onto the slot's lower edge would collapse it).
    /// Measured while the slot is closed, so the shift is applied explicitly.
    /// Returns null when the ungrouped section already has tasks — then it's a real
    /// drop target on its own (cross-group row drop) and the slot is visual noise.</summary>
    private double? FindUngroupedZoneBottom()
    {
        // Ungrouped tasks present → no slot; the section already accepts drops.
        if (ViewModel.GroupedTaskList.FirstOrDefault()?.ShowEmptyUngroupedHint != true)
            return null;
        var container = GroupedTasksControl.ItemContainerGenerator.ContainerFromIndex(1) as FrameworkElement;
        if (container == null) return null;
        var header = FindVisualChild<Border>(container, b => b.Tag is string s && s == "TaskGroupHeader");
        if (header == null || header.ActualHeight <= 0) return null;
        return header.TranslatePoint(new Point(0, 0), TaskAreaPanel).Y
            + UngroupedSlotHeight
            + header.ActualHeight / 2;
    }

    /// <summary>Depth-first search for the first descendant element matching the predicate.</summary>
    private static T? FindVisualChild<T>(DependencyObject root, Func<T, bool>? predicate = null)
        where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match && (predicate == null || predicate(match))) return match;
            if (FindVisualChild(child, predicate) is T found) return found;
        }
        return null;
    }

    // Task row reorder drop handlers
    private void TaskRow_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskItem)) && sender is Border border)
        {
            UpdateTaskRowDropIndicator(border, e);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void TaskRow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskItem)) && sender is Border border)
        {
            UpdateTaskRowDropIndicator(border, e);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    /// <summary>Top line on the upper half of the row (insert before), bottom line on
    /// the lower half (insert after) — matching the drop behavior.</summary>
    private static void UpdateTaskRowDropIndicator(Border border, DragEventArgs e)
    {
        var brush = (Brush)Application.Current.FindResource("AccentBlue");
        bool lowerHalf = e.GetPosition(border).Y > border.ActualHeight / 2;
        border.BorderBrush = brush;
        border.BorderThickness = new Thickness(0, lowerHalf ? 0 : 2, 0, lowerHalf ? 2 : 0);
    }

    private void TaskRow_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Colors.Transparent);
            border.BorderThickness = new Thickness(0);
        }
        e.Handled = true;
    }

    private void TaskRow_Drop(object sender, DragEventArgs e)
    {
        SuppressPendingTaskClick();
        if (sender is Border border && border.DataContext is TaskItem targetTask
            && e.Data.GetDataPresent(typeof(TaskItem)))
        {
            border.BorderBrush = new SolidColorBrush(Colors.Transparent);
            border.BorderThickness = new Thickness(0);

            var draggedTask = e.Data.GetData(typeof(TaskItem)) as TaskItem;
            if (draggedTask == null || draggedTask.Id == targetTask.Id) return;

            // Reorder only within the same list — cross-list moves use the sidebar.
            if (draggedTask.ListId != targetTask.ListId) return;

            // Move dragged task to target's position. When the row's group differs, the
            // drop retargets the task into that group — which is how a grouped task gets
            // dragged back to ungrouped (drop on an ungrouped row) and vice versa.
            var siblings = ViewModel.Tasks
                .Where(t => t.ListId == targetTask.ListId
                    && t.GroupId == targetTask.GroupId
                    && !t.IsClosed)
                .OrderBy(t => t.Order)
                .ToList();

            bool crossGroup = draggedTask.GroupId != targetTask.GroupId;
            if (crossGroup)
            {
                // The dragged task isn't in the target group's sibling list yet — add it
                // so ReorderService can pin it to the drop position in that group.
                if (!siblings.Contains(draggedTask)) siblings.Add(draggedTask);
            }

            // Upper half of the target row inserts before it, lower half after it
            bool lowerHalf = e.GetPosition(border).Y > border.ActualHeight / 2;
            if (!ReorderService.Reorder(siblings, draggedTask, targetTask, lowerHalf)) return;

            if (crossGroup)
            {
                draggedTask.GroupId = targetTask.GroupId;
                draggedTask.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            // Save all
            foreach (var t in siblings)
                App.Database!.Tasks.Update(t);

            ViewModel.Refresh();
        }
        e.Handled = true;
    }

    private void GroupHeader_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border border)
            UpdateGroupHeaderDropVisual(border, e);
        e.Handled = true;
    }

    private void GroupHeader_DragOver(object sender, DragEventArgs e)
    {
        if (sender is Border border)
            UpdateGroupHeaderDropVisual(border, e);
        e.Handled = true;
    }

    private void GroupHeader_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
            ClearGroupHeaderDropVisual(border);
        e.Handled = true;
    }

    /// <summary>Task drop → background highlight; group reorder → top/bottom insert line.</summary>
    private void UpdateGroupHeaderDropVisual(Border border, DragEventArgs e)
    {
        ClearGroupHeaderDropVisual(border);
        if (e.Data.GetDataPresent(typeof(TaskItem)))
        {
            border.Background = (Brush)Application.Current.FindResource("AccentBlueLight");
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(typeof(TaskGroup)) && e.Data.GetData(typeof(TaskGroup)) is TaskGroup draggedGroup
            && border.DataContext is GroupedTasks gt && gt.HasGroup && draggedGroup.Id != gt.Group!.Id)
        {
            bool lowerHalf = e.GetPosition(border).Y > border.ActualHeight / 2;
            border.BorderBrush = (Brush)Application.Current.FindResource("AccentBlue");
            border.BorderThickness = new Thickness(0, lowerHalf ? 0 : 2, 0, lowerHalf ? 2 : 0);
            e.Effects = DragDropEffects.Move;
        }
    }

    private void ClearGroupHeaderDropVisual(Border border)
    {
        border.Background = Brushes.Transparent;
        border.BorderBrush = Brushes.Transparent;
        border.BorderThickness = new Thickness(0);
    }

    private void GroupHeader_Drop(object sender, DragEventArgs e)
    {
        SuppressPendingTaskClick();
        if (sender is Border border)
        {
            ClearGroupHeaderDropVisual(border);
            if (border.DataContext is GroupedTasks gt && gt.HasGroup)
            {
                if (e.Data.GetDataPresent(typeof(TaskItem)) && e.Data.GetData(typeof(TaskItem)) is TaskItem task
                    && task.GroupId != gt.Group!.Id)
                {
                    SetGroupHeaderToggleSuppressed();
                    ViewModel.MoveTaskToGroupCommand.Execute((task, gt.Group));
                }
                else if (e.Data.GetDataPresent(typeof(TaskGroup)) && e.Data.GetData(typeof(TaskGroup)) is TaskGroup draggedGroup
                    && draggedGroup.Id != gt.Group!.Id)
                {
                    SetGroupHeaderToggleSuppressed();
                    ReorderTaskGroups(border, e, draggedGroup, gt.Group!);
                }
            }
        }
        e.Handled = true;
    }

    private void ReorderTaskGroups(Border border, DragEventArgs e, TaskGroup dragged, TaskGroup target)
    {
        var siblings = ViewModel.Groups.Where(g => g.ListId == target.ListId).OrderBy(g => g.Order).ToList();
        // Upper half of the target header inserts before it, lower half after it
        bool lowerHalf = e.GetPosition(border).Y > border.ActualHeight / 2;
        if (!ReorderService.Reorder(siblings, dragged, target, lowerHalf)) return;
        foreach (var g in siblings)
            App.Database!.Groups.Update(g);

        ViewModel.RefreshActiveTasks();
    }

    // Section-level drop (for both grouped and ungrouped areas)
    private void GroupSection_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskItem)) && sender is Border border)
        {
            border.Background = (Brush)Application.Current.FindResource("AccentBlueLight");
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void GroupSection_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
            border.Background = Brushes.Transparent;
        e.Handled = true;
    }

    private void GroupSection_Drop(object sender, DragEventArgs e)
    {
        SuppressPendingTaskClick();
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
            if (e.Data.GetDataPresent(typeof(TaskItem)) && border.DataContext is GroupedTasks gt)
            {
                var task = e.Data.GetData(typeof(TaskItem)) as TaskItem;
                if (task != null)
                {
                    var targetGroup = gt.HasGroup ? gt.Group : null;
                    if (task.GroupId != targetGroup?.Id)
                        ViewModel.MoveTaskToGroupCommand.Execute((task, targetGroup));
                }
            }
        }
        e.Handled = true;
    }

    // ─── List Header: emoji + rename ─────────────────────
    private void ListEmoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || ViewModel.ActiveList == null) return;

        var emojis = new[] {
            "📋","📝","📌","📎","📁","📂","📊","📈","📅","📆",
            "💼","🏠","🏢","🏪","🛒","🛍️","📚","📖","✏️","🖊️",
            "🎯","🎯","💡","⭐","🌟","❤️","💚","💙","💛","💜",
            "🎵","🎶","🏃","🚶","🚗","✈️","🚲","💻","📱","🖥️",
            "🎮","🎨","🎬","🎭","📷","📸","💰","💳","💵","🔧",
            "🔨","🔑","🔔","📢","💬","🗨️","✅","❌","⚠️","⏰",
            "⌛","☕","🍔","🍕","🌮","🎂","🍺","🥤","🌍","🏖️",
            "⛰️","🌲","🐱","🐶","🦊","🐼","👤","👥","💪","🧠",
            "≡","☰","⋯","∷"
        };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
        };
        var panel = new WrapPanel
        {
            Width = 400,
            Background = (Brush)Application.Current.FindResource("CardBackgroundBrush"),
        };
        foreach (var em in emojis)
        {
            var embtn = new Button { Content = em, FontSize = 20, Width = 38, Height = 38,
                Margin = new Thickness(2), FontFamily = new FontFamily("Segoe UI Emoji"),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand };
            var captured = em;
            embtn.Click += (s, _) =>
            {
                ViewModel.ActiveList!.Icon = captured;
                App.Database!.Lists.Update(ViewModel.ActiveList);
                popup.IsOpen = false;
            };
            panel.Children.Add(embtn);
        }
        popup.Child = panel;
        popup.IsOpen = true;
    }

    private void ListTitle_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.ActiveList == null || ViewModel.ActiveList.IsSystem) return;
        // Match the title label's adaptive color on the themed header (ADR-014).
        ListTitleEdit.Foreground = ViewModel.HeaderTitleLight switch
        {
            true => new SolidColorBrush(Colors.White),
            false => new SolidColorBrush(Color.FromRgb(0x20, 0x1F, 0x1E)),
            _ => (Brush)Application.Current.FindResource("TextPrimaryBrush"),
        };
        ListTitleLabel.Visibility = Visibility.Collapsed;
        ListTitleEdit.Text = ViewModel.ActiveList.Name;
        ListTitleEdit.Visibility = Visibility.Visible;
        ListTitleEdit.Focus();
        ListTitleEdit.SelectAll();
    }

    private void ListTitleEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitListTitle(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelListTitle(); e.Handled = true; }
    }

    private void ListTitleEdit_LostFocus(object sender, RoutedEventArgs e) => CommitListTitle();

    private void CommitListTitle()
    {
        if (ViewModel.ActiveList == null) return;
        var newName = ListTitleEdit.Text.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != ViewModel.ActiveList.Name)
        {
            ViewModel.ActiveList.Name = newName;
            ViewModel.RenameListCommand.Execute(ViewModel.ActiveList);
            ViewModel.NotifyHeaderTitleChanged();
        }
        CancelListTitle();
    }

    private void CancelListTitle()
    {
        ListTitleEdit.Visibility = Visibility.Collapsed;
        ListTitleLabel.Visibility = Visibility.Visible;
    }

    // ─── List Menu ────────────────────────────────────────
    // WPF opens a ContextMenu on right-click only; the header's three-dot button
    // opens it on left-click via this handler (same pattern as the other "more" menus).
    private void ListMore_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu is { } menu)
        {
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }
    }

    private void ListMenu_Rename(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveList != null)
            ViewModel.RenameListCommand.Execute(ViewModel.ActiveList);
    }

    private void ListMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveList is not { } list) return;
        if (FluentDialog.Confirm(Window.GetWindow(this), Loc.ConfirmDeleteMsg(list.Name), Loc.ConfirmDelete))
            ViewModel.DeleteListCommand.Execute(list);
    }

    private void ListMenu_Theme(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveList is { } list)
            OpenListThemeDialog(list);
    }

    private void OpenListThemeDialog(TaskList list)
    {
        var dialog = new ListThemeDialog(list) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
