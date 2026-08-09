using System.ComponentModel;
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
                RefreshDetailPickers();
        };

        SearchBox.TextChanged += (s, e) =>
        {
            var hasText = !string.IsNullOrEmpty(SearchBox.Text);
            SearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            SearchClearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        };
        UpdateAddTaskPlaceholder();
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
                if (FluentDialog.Confirm(this, Loc.ConfirmDeleteGroupMsg(lgd.Group.Name), Loc.DeleteGroup))
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
        menu.Items.Add(new Separator());
        var d = new MenuItem { Header = Loc.Delete, Tag = list };
        d.Click += (_, _) => { if (FluentDialog.Confirm(this, Loc.ConfirmDeleteMsg(list.Name), Loc.ConfirmDelete)) ViewModel.DeleteListCommand.Execute(list); };
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

    private void SidebarCtx_Delete(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is TaskList list)
        {
            if (FluentDialog.Confirm(this, Loc.ConfirmDeleteMsg(list.Name), Loc.ConfirmDelete))
                ViewModel.DeleteListCommand.Execute(list);
        }
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
            ViewModel.CloseTaskCommand.Execute((task, CloseMode.Complete));
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
                    ViewModel.CloseTaskCommand.Execute((task, CloseMode.Complete));
                menu.Items.Add(completeItem);

                var cancelItem = new MenuItem { Header = Loc.Cancel };
                cancelItem.Click += (s, _) =>
                    ViewModel.CloseTaskCommand.Execute((task, CloseMode.Cancel));
                menu.Items.Add(cancelItem);

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

    // ─── List Menu ────────────────────────────────────────
    private void ListMenu_Rename(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveList != null)
            ViewModel.RenameListCommand.Execute(ViewModel.ActiveList);
    }

    private void ListMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ActiveList != null)
            ViewModel.DeleteListCommand.Execute(ViewModel.ActiveList);
    }

    // ─── Dialogs ──────────────────────────────────────────
    private void OpenEditCloseTimeDialog(TaskItem task)
    {
        if (task.CloseRecord == null) return;
        var dialog = new DateTimeDialog(task.CloseRecord.ClosedAt) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Saved)
            ViewModel.EditCloseTimeCommand.Execute((task, dialog.ResultTimestamp));
    }

    // ─── Detail Pane ──────────────────────────────────────
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
            var dialog = new Views.Dialogs.DateTimeDialog(
                ViewModel.SelectedTask!.DueDate ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            { Owner = this, Title = Loc.Date };
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
            ("1 " + (Loc.Language == AppLanguage.Chinese ? "小时后" : "hour later"), 1.0),
            ("3 " + (Loc.Language == AppLanguage.Chinese ? "小时后" : "hours later"), 3.0),
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
            var dlg = new Views.Dialogs.DateTimeDialog(
                ViewModel.SelectedTask!.Reminder ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                includeTime: true)
            { Owner = this, Title = "Reminder" };
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

    private void DetailDueDate_Clear(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.DueDate = null;
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

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

    private void StepDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskStep step
            && ViewModel.SelectedTask != null)
        {
            ViewModel.DeleteStepCommand.Execute((ViewModel.SelectedTask, step));
        }
    }

    // ─── Detail Pane: Tags & Group ────────────────────────
    public void RefreshDetailPickers()
    {
        var task = ViewModel.SelectedTask;
        if (task == null) return;

        _suppressDetailEvents = true;

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
