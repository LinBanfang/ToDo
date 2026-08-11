using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo;

public partial class MainWindow
{
    // ─── Sidebar List Drag & Drop ────────────────────────
    // TaskItem → move the task to the target list
    // TaskList → reorder the list within its sidebar area (half-zone insertion)
    private ListBoxItem? _lastSidebarDropItem;

    private void SidebarList_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is ListBox listBox)
            UpdateSidebarDragState(listBox, e);
        e.Handled = true;
    }

    private void SidebarList_DragOver(object sender, DragEventArgs e)
    {
        if (sender is ListBox listBox)
            UpdateSidebarDragState(listBox, e);
        e.Handled = true;
    }

    private void UpdateSidebarDragState(ListBox listBox, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskItem)))
        {
            e.Effects = DragDropEffects.Move;
            return;
        }

        if (e.Data.GetDataPresent(typeof(TaskList)) && e.Data.GetData(typeof(TaskList)) is TaskList dragged)
        {
            // Lists can only be reordered within the same sidebar area
            bool sameArea = listBox.Items.Cast<TaskList>().Contains(dragged) && !dragged.IsSystem;
            if (sameArea)
            {
                e.Effects = DragDropEffects.Move;
                UpdateSidebarDropIndicator(listBox, e);
            }
        }
    }

    private void SidebarList_DragLeave(object sender, DragEventArgs e)
    {
        ClearSidebarDropIndicator();
        e.Handled = true;
    }

    private void SidebarList_Drop(object sender, DragEventArgs e)
    {
        ClearSidebarDropIndicator();
        if (sender is not ListBox listBox) { e.Handled = true; return; }

        var item = HitTestSidebarItem(listBox, e);
        if (item?.DataContext is not TaskList targetList) { e.Handled = true; return; }

        if (e.Data.GetDataPresent(typeof(TaskItem)) && e.Data.GetData(typeof(TaskItem)) is TaskItem task)
        {
            // Move a task to this list
            if (targetList.Id != task.ListId)
                ViewModel.MoveTaskToListCommand.Execute((task, targetList));
        }
        else if (e.Data.GetDataPresent(typeof(TaskList)) && e.Data.GetData(typeof(TaskList)) is TaskList draggedList)
        {
            // Reorder a sidebar list within the same area (half-zone insertion)
            if (draggedList.Id != targetList.Id)
                ReorderSidebarList(listBox, e, draggedList, item!);
        }
        e.Handled = true;
    }

    private static ListBoxItem? HitTestSidebarItem(ListBox listBox, DragEventArgs e)
    {
        var pos = e.GetPosition(listBox);
        var element = listBox.InputHitTest(pos) as DependencyObject;
        while (element != null && element is not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);
        return element as ListBoxItem;
    }

    private void UpdateSidebarDropIndicator(ListBox listBox, DragEventArgs e)
    {
        ClearSidebarDropIndicator();
        // Only lists show an insert position; task drops are a plain "move to list"
        if (!e.Data.GetDataPresent(typeof(TaskList))) return;
        var item = HitTestSidebarItem(listBox, e);
        if (item == null) return;

        bool lowerHalf = e.GetPosition(item).Y > item.ActualHeight / 2;
        item.BorderBrush = (Brush)Application.Current.FindResource("AccentBlue");
        item.BorderThickness = new Thickness(0, lowerHalf ? 0 : 2, 0, lowerHalf ? 2 : 0);
        _lastSidebarDropItem = item;
    }

    private void ClearSidebarDropIndicator()
    {
        if (_lastSidebarDropItem != null)
        {
            _lastSidebarDropItem.BorderBrush = Brushes.Transparent;
            _lastSidebarDropItem.BorderThickness = new Thickness(0);
            _lastSidebarDropItem = null;
        }
    }

    private void ReorderSidebarList(ListBox listBox, DragEventArgs e, TaskList dragged, ListBoxItem targetItem)
    {
        var siblings = listBox.Items.Cast<TaskList>()
            .Where(l => !l.IsSystem) // system lists stay in fixed order
            .OrderBy(l => l.Order)
            .ToList();

        // Upper half of the target row inserts before it, lower half after it
        bool lowerHalf = e.GetPosition(targetItem).Y > targetItem.ActualHeight / 2;
        if (!ReorderService.Reorder(siblings, dragged, (TaskList)targetItem.DataContext!, lowerHalf)) return;

        foreach (var l in siblings)
            App.Database!.Lists.Update(l);

        ViewModel.Refresh();
    }

    // ─── Drag & Drop ──────────────────────────────────────
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
}
