using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo;

public partial class MainWindow
{
    // ─── Main Content ─────────────────────────────────────
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
                if (FluentDialog.Confirm(this, Loc.ConfirmDeleteGroupMsg(gt.Group!.Name), Loc.DeleteGroup))
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
                if (FluentDialog.Confirm(this, Loc.ConfirmDeleteMsg(task.Title), Loc.ConfirmDelete))
                    ViewModel.DeleteTaskCommand.Execute(task);
            };
            menu.Items.Add(deleteItem);
        }
    }

}
