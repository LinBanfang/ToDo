using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo.Views;

public partial class SidebarControl : UserControl
{
    private MainViewModel ViewModel => DataContext as MainViewModel ?? App.ViewModel!;
    private Point _dragStartPoint;

    public SidebarControl()
    {
        InitializeComponent();
        SearchBox.TextChanged += (s, e) =>
        {
            var hasText = !string.IsNullOrEmpty(SearchBox.Text);
            SearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            SearchClearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    // ─── Sidebar ──────────────────────────────────────────
    private void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        ViewModel.SearchQuery = "";
    }

    private void NewListGroup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateListGroupCommand.Execute(Loc.NewGroup);
    }

    private void StickyNote_Click(object sender, RoutedEventArgs e) => WindowManager.OpenSticky();

    private bool _suppressListGroupHeaderToggle;

    private void ListGroupHeader_Click(object sender, MouseButtonEventArgs e)
    {
        // Consume suppression set when a group drag or a drop onto the header ends
        if (_suppressListGroupHeaderToggle) { _suppressListGroupHeaderToggle = false; return; }
        if (sender is not FrameworkElement fe || fe.DataContext is not ListGroupDisplay lgd) return;
        // The name is double-click-to-rename; toggling here would rebuild the list and
        // reset ClickCount, breaking the rename. Also stay out of the way while editing.
        if (lgd.IsEditing || IsInsideTaggedElement(e.OriginalSource, "ListGroupName")) return;
        ViewModel.ToggleListGroupCollapseCommand.Execute(lgd.Group);
    }

    private void ListGroupHeader_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe
            && fe.DataContext is ListGroupDisplay lgd && !lgd.IsEditing
            && DragThresholdExceeded(e))
        {
            SetListGroupHeaderToggleSuppressed();
            DragDrop.DoDragDrop(fe, lgd.Group, DragDropEffects.Move);
        }
    }

    private void SetListGroupHeaderToggleSuppressed()
    {
        _suppressListGroupHeaderToggle = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _suppressListGroupHeaderToggle = false);
    }

    private void ListGroup_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is Border border && border.DataContext is ListGroupDisplay lgd)
        {
            var menu = border.ContextMenu;
            if (menu == null) return;
            menu.Items.Clear();
            var rename = new MenuItem { Header = Loc.Rename };
            rename.Click += (s, _) =>
            {
                lgd.EditName = lgd.Group.Name;
                lgd.IsEditing = true;
            };
            menu.Items.Add(rename);
            var delete = new MenuItem { Header = Loc.DeleteGroup };
            delete.Click += (s, _) =>
            {
                if (FluentDialog.Confirm(Window.GetWindow(this), Loc.ConfirmDeleteGroupMsg(lgd.Group.Name), Loc.DeleteGroup))
                    ViewModel.DeleteListGroupCommand.Execute(lgd.Group);
            };
            menu.Items.Add(delete);
        }
    }

    public void SidebarGrid_MenuLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.PlacementTarget is not Grid grid
            || grid.DataContext is not TaskList list || list.IsSystem) return;

        menu.Items.Clear();
        BuildMenu(menu, list);
    }

    public void SidebarGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Grid grid) return;
        var menu = grid.ContextMenu;
        if (menu == null) return;
        if (grid.DataContext is not TaskList list || list.IsSystem) return;

        menu.Items.Clear();
        BuildMenu(menu, list);
    }

    private void BuildMenu(ContextMenu menu, TaskList list)
    {
        var moveItem = new MenuItem { Header = Loc.MoveToGroup };
        if (list.GroupId != null)
        {
            var u = new MenuItem { Header = Loc.Ungrouped };
            u.Click += (_, _) => ViewModel.MoveListToGroupCommand.Execute((list, null));
            moveItem.Items.Add(u);
            moveItem.Items.Add(new Separator());
        }
        foreach (var g in ViewModel.ListGroups)
        {
            if (g.Id == list.GroupId) continue;
            var gi = new MenuItem { Header = g.Name };
            var cg = g;
            gi.Click += (_, _) => ViewModel.MoveListToGroupCommand.Execute((list, cg));
            moveItem.Items.Add(gi);
        }
        menu.Items.Add(moveItem);

        var r = new MenuItem { Header = Loc.Rename, Tag = list };
        r.Click += (_, _) =>
        {
            list.EditName = list.Name;
            list.IsRenaming = true;
        };
        menu.Items.Add(r);

        var t = new MenuItem { Header = Loc.ListTheme, Tag = list };
        t.Click += (_, _) => OpenListThemeDialog(list);
        menu.Items.Add(t);

        menu.Items.Add(new Separator());
        var d = new MenuItem { Header = Loc.Delete, Tag = list };
        d.Click += (_, _) => { if (FluentDialog.Confirm(Window.GetWindow(this), Loc.ConfirmDeleteMsg(list.Name), Loc.ConfirmDelete)) ViewModel.DeleteListCommand.Execute(list); };
        menu.Items.Add(d);
    }

    private void SidebarListName_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is FrameworkElement fe && fe.DataContext is TaskList list && !list.IsSystem)
        {
            list.EditName = list.Name;
            list.IsRenaming = true;
            e.Handled = true;
        }
    }

    private void SidebarRename_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Auto-focus the rename box so typing works immediately and clicking away
        // commits (via LostFocus) instead of leaving the list stuck in rename mode.
        if (sender is TextBox tb && tb.IsVisible)
            Dispatcher.BeginInvoke(() => { tb.Focus(); tb.SelectAll(); });
    }

    private void SidebarRename_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskList list)
        {
            if (e.Key == Key.Enter) { CommitSidebarRename(list); e.Handled = true; }
            else if (e.Key == Key.Escape) { list.IsRenaming = false; e.Handled = true; }
        }
    }

    private void SidebarRename_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskList list)
            CommitSidebarRename(list);
    }

    private void CommitSidebarRename(TaskList list)
    {
        var n = list.EditName?.Trim();
        if (!string.IsNullOrEmpty(n) && n != list.Name)
        {
            list.Name = n;
            ViewModel.RenameListCommand.Execute(list);
            ViewModel.NotifyHeaderTitleChanged();
        }
        list.IsRenaming = false;
    }

    // ─── List group rename handlers ───────────────────────
    private void ListGroupName_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is FrameworkElement fe && fe.DataContext is ListGroupDisplay lgd)
        { lgd.EditName = lgd.Group.Name; lgd.IsEditing = true; e.Handled = true; }
    }
    private void ListGroupName_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ListGroupDisplay lgd)
        { if (e.Key == Key.Enter) { CommitListGroupRename(lgd); e.Handled = true; } else if (e.Key == Key.Escape) { lgd.IsEditing = false; e.Handled = true; } }
    }
    private void ListGroupName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ListGroupDisplay lgd) CommitListGroupRename(lgd);
    }
    private void CommitListGroupRename(ListGroupDisplay lgd)
    {
        var n = lgd.EditName?.Trim();
        if (!string.IsNullOrEmpty(n) && n != lgd.Group.Name) { lgd.Group.Name = n; ViewModel.RenameListGroupCommand.Execute(lgd.Group); }
        lgd.IsEditing = false;
    }

    // ─── Drag list to group / reorder group handlers ──────
    private void ListGroupHeader_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border b)
            UpdateListGroupHeaderDropVisual(b, e);
        e.Handled = true;
    }

    private void ListGroupHeader_DragOver(object sender, DragEventArgs e)
    {
        if (sender is Border b)
            UpdateListGroupHeaderDropVisual(b, e);
        e.Handled = true;
    }

    private void ListGroupHeader_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b)
            ClearListGroupHeaderDropVisual(b);
        e.Handled = true;
    }

    /// <summary>List drop → background highlight; group reorder → top/bottom insert line.</summary>
    private void UpdateListGroupHeaderDropVisual(Border b, DragEventArgs e)
    {
        ClearListGroupHeaderDropVisual(b);
        if (e.Data.GetDataPresent(typeof(TaskList)))
        {
            b.Background = (Brush)Application.Current.FindResource("AccentBlueLight");
            e.Effects = DragDropEffects.Move;
        }
        else if (e.Data.GetDataPresent(typeof(ListGroup)) && e.Data.GetData(typeof(ListGroup)) is ListGroup draggedGroup
            && b.DataContext is ListGroupDisplay lgd && draggedGroup.Id != lgd.Group.Id)
        {
            bool lowerHalf = e.GetPosition(b).Y > b.ActualHeight / 2;
            b.BorderBrush = (Brush)Application.Current.FindResource("AccentBlue");
            b.BorderThickness = new Thickness(0, lowerHalf ? 0 : 2, 0, lowerHalf ? 2 : 0);
            e.Effects = DragDropEffects.Move;
        }
    }

    private void ClearListGroupHeaderDropVisual(Border b)
    {
        b.Background = Brushes.Transparent;
        b.BorderBrush = Brushes.Transparent;
        b.BorderThickness = new Thickness(0);
    }

    private void ListGroupHeader_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border b)
        {
            ClearListGroupHeaderDropVisual(b);
            if (b.DataContext is ListGroupDisplay lgd)
            {
                if (e.Data.GetDataPresent(typeof(TaskList)) && e.Data.GetData(typeof(TaskList)) is TaskList list
                    && list.GroupId != lgd.Group.Id)
                {
                    SetListGroupHeaderToggleSuppressed();
                    ViewModel.MoveListToGroupCommand.Execute((list, lgd.Group));
                }
                else if (e.Data.GetDataPresent(typeof(ListGroup)) && e.Data.GetData(typeof(ListGroup)) is ListGroup draggedGroup
                    && draggedGroup.Id != lgd.Group.Id)
                {
                    SetListGroupHeaderToggleSuppressed();
                    ReorderListGroups(b, e, draggedGroup, lgd.Group);
                }
            }
        }
        e.Handled = true;
    }

    private void ReorderListGroups(Border b, DragEventArgs e, ListGroup dragged, ListGroup target)
    {
        var siblings = ViewModel.ListGroups.OrderBy(g => g.Order).ToList();
        // Upper half of the target header inserts before it, lower half after it
        bool lowerHalf = e.GetPosition(b).Y > b.ActualHeight / 2;
        if (!ReorderService.Reorder(siblings, dragged, target, lowerHalf)) return;
        foreach (var g in siblings)
            App.Database!.ListGroups.Update(g);

        ViewModel.Refresh();
    }

    // ─── Drag sidebar list items ──────────────────────────
    public void SidebarList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !DragThresholdExceeded(e) || sender is not ListBox lb) return;
        var pos = e.GetPosition(lb);
        var el = lb.InputHitTest(pos) as DependencyObject;
        while (el != null)
        {
            if (el is TextBox) return; // don't drag while editing
            if (el is ListBoxItem) break;
            el = VisualTreeHelper.GetParent(el);
        }
        if (el is ListBoxItem lbi && lbi.DataContext is TaskList list && !list.IsRenaming)
            DragDrop.DoDragDrop(lbi, list, DragDropEffects.Move);
    }

    private void NewList_Click(object sender, RoutedEventArgs e)
    {
        var name = Loc.NewListName;
        ViewModel.CreateListCommand.Execute(name);
        // Find the newly created list by id and put it in rename mode
        var newList = ViewModel.CustomLists.FirstOrDefault(l => l.Id == ViewModel.LastCreatedListId);
        if (newList != null)
        {
            ViewModel.ActiveListId = newList.Id;
            newList.EditName = name;
            newList.IsRenaming = true;
        }
    }

    // ─── List Theme Dialog ────────────────────────────────
    private void OpenListThemeDialog(TaskList list)
    {
        var dialog = new ListThemeDialog(list) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

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

    // ─── Drag helpers ─────────────────────────────────────
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
}
