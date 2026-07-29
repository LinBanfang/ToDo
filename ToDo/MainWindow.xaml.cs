using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    private bool _suppressDetailEvents;
    private DateTime _lastDropTime = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.ViewModel;
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
    }

    // ─── Sidebar ──────────────────────────────────────────
    private void SearchClear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        ViewModel.SearchQuery = "";
    }

    private void AddList_Click(object sender, RoutedEventArgs e)
    {
        NewListBox.Focus();
    }

    private void NewListGroup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateListGroupCommand.Execute("New group");
    }

    private void ListGroupHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ListGroupDisplay lgd)
            ViewModel.ToggleListGroupCollapseCommand.Execute(lgd.Group);
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
            menu.Items.Add(new Separator());
            var delete = new MenuItem { Header = Loc.DeleteListGroup };
            delete.Click += (s, _) =>
            {
                if (MessageBox.Show(Loc.ConfirmDeleteListGroupMsg(lgd.Group.Name), Loc.DeleteListGroup,
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                    ViewModel.DeleteListGroupCommand.Execute(lgd.Group);
            };
            menu.Items.Add(delete);
        }
    }

    private void ListGroupName_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2 && sender is FrameworkElement fe && fe.DataContext is ListGroupDisplay lgd)
        {
            lgd.EditName = lgd.Group.Name;
            lgd.IsEditing = true;
            e.Handled = true;
        }
    }

    private void ListGroupName_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ListGroupDisplay lgd)
        {
            if (e.Key == Key.Enter) { CommitListGroupRename(lgd); e.Handled = true; }
            else if (e.Key == Key.Escape) { lgd.IsEditing = false; e.Handled = true; }
        }
    }

    private void ListGroupName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ListGroupDisplay lgd)
            CommitListGroupRename(lgd);
    }

    private void CommitListGroupRename(ListGroupDisplay lgd)
    {
        var newName = lgd.EditName?.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != lgd.Group.Name)
        {
            lgd.Group.Name = newName;
            ViewModel.RenameListGroupCommand.Execute(lgd.Group);
        }
        lgd.IsEditing = false;
    }

    // Drag list to group
    private void ListGroupHeader_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskList)) && sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF2, 0xFC));
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void ListGroupHeader_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border) border.Background = Brushes.Transparent;
        e.Handled = true;
    }

    private void ListGroupHeader_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
            if (e.Data.GetDataPresent(typeof(TaskList)) && border.DataContext is ListGroupDisplay lgd)
            {
                var list = e.Data.GetData(typeof(TaskList)) as TaskList;
                if (list != null && list.GroupId != lgd.Group.Id)
                    ViewModel.MoveListToGroupCommand.Execute((list, lgd.Group));
            }
        }
        e.Handled = true;
    }

    public void UngroupedList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBox lb) return;

        // Find and select the right-clicked item
        var pos = Mouse.GetPosition(lb);
        var hit = lb.InputHitTest(pos) as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);

        if (hit is ListBoxItem lbi && lbi.DataContext is TaskList list && !list.IsSystem)
        {
            lbi.IsSelected = true;
            var menu = new ContextMenu();
            BuildSidebarListMenu(menu, list);
            lb.ContextMenu = menu;
        }
        else
        {
            lb.ContextMenu = null;
            e.Handled = true;
        }
    }

    public void SidebarList_PreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb) return;
        var pos = e.GetPosition(lb);
        var hit = lb.InputHitTest(pos) as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);
        if (hit is not ListBoxItem lbi || lbi.DataContext is not TaskList list || list.IsSystem) return;

        var menu = new ContextMenu();
        BuildSidebarListMenu(menu, list);
        menu.PlacementTarget = lbi;
        menu.IsOpen = true;
        e.Handled = true;
    }

    public void SidebarListGrid_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid grid || grid.DataContext is not TaskList list) return;
        var menu = new ContextMenu();
        BuildSidebarListMenu(menu, list);
        menu.PlacementTarget = grid;
        menu.IsOpen = true;
        e.Handled = true;
    }

    public void SidebarListItem_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb) return;
        var pos = e.GetPosition(lb);
        var hit = lb.InputHitTest(pos) as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
            hit = VisualTreeHelper.GetParent(hit);
        if (hit is not ListBoxItem lbi || lbi.DataContext is not TaskList list) return;

        lbi.IsSelected = true;
        var menu = new ContextMenu();
        BuildSidebarListMenu(menu, list);
        menu.PlacementTarget = lbi;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void BuildSidebarListMenu(ContextMenu menu, TaskList list)
    {
        menu.Items.Clear();

        // Move to group submenu
        var moveMenu = new MenuItem { Header = Loc.MoveToGroup };
        if (list.GroupId != null)
        {
            var ungroupItem = new MenuItem { Header = Loc.Ungrouped };
            ungroupItem.Click += (s, _) => ViewModel.MoveListToGroupCommand.Execute((list, null));
            moveMenu.Items.Add(ungroupItem);
            moveMenu.Items.Add(new Separator());
        }
        foreach (var g in ViewModel.ListGroups)
        {
            if (g.Id == list.GroupId) continue;
            var gi = new MenuItem { Header = g.Name };
            var captured = g;
            gi.Click += (s, _) => ViewModel.MoveListToGroupCommand.Execute((list, captured));
            moveMenu.Items.Add(gi);
        }
        menu.Items.Add(moveMenu);
        menu.Items.Add(new Separator());

        var renameItem = new MenuItem { Header = Loc.RenameList };
        renameItem.Click += (s, _) => { /* rename handled by ListTitle_Click */ };
        menu.Items.Add(renameItem);

        var deleteItem = new MenuItem { Header = Loc.DeleteList };
        deleteItem.Click += (s, _) =>
        {
            if (MessageBox.Show(Loc.ConfirmDeleteMsg(list.Name), Loc.ConfirmDelete,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                ViewModel.DeleteListCommand.Execute(list);
        };
        menu.Items.Add(deleteItem);
    }

    public void SidebarList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is ListBox lb)
        {
            var pos = Mouse.GetPosition(lb);
            var element = lb.InputHitTest(pos) as DependencyObject;
            while (element != null && element is not ListBoxItem)
                element = VisualTreeHelper.GetParent(element);
            if (element is ListBoxItem lbi && lbi.DataContext is TaskList list)
                DragDrop.DoDragDrop(lbi, list, DragDropEffects.Move);
        }
    }

    private void NewListBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            ViewModel.CreateListCommand.Execute(tb.Text.Trim());
            tb.Text = "";
            e.Handled = true;
        }
    }

    private void TagManage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new TagManageDialog { Owner = this };
        dialog.ShowDialog();
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
        var panel = new WrapPanel { Width = 400, Background = new SolidColorBrush(Colors.White) };
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
        }
        CancelListTitle();
    }

    private void CancelListTitle()
    {
        ListTitleEdit.Visibility = Visibility.Collapsed;
        ListTitleLabel.Visibility = Visibility.Visible;
    }

    // ─── Main Content ─────────────────────────────────────
    private void AddTaskBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && !string.IsNullOrWhiteSpace(tb.Text))
        {
            ViewModel.CreateTaskCommand.Execute(tb.Text.Trim());
            tb.Text = "";
            e.Handled = true;
        }
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CreateGroupCommand.Execute(Loc.NewGroup);
    }

    private void LanguageToggle_Click(object sender, RoutedEventArgs e)
    {
        Loc.Toggle();
        // Restart window to apply language
        var newWindow = new MainWindow();
        newWindow.Show();
        Close();
    }

    private void GroupHeader_Toggle(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2) return;
        if (sender is FrameworkElement fe && fe.DataContext is GroupedTasks gt)
        {
            ViewModel.ToggleGroupCollapseCommand.Execute(gt.Group);
        }
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
                if (MessageBox.Show(Loc.ConfirmDeleteGroupMsg(gt.Group!.Name),
                        Loc.DeleteGroup, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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

    // ─── Sidebar List Drop (move task to another list) ───
    private void SidebarList_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskItem)))
            e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void SidebarList_DragLeave(object sender, DragEventArgs e) => e.Handled = true;

    private void SidebarList_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox listBox || !e.Data.GetDataPresent(typeof(TaskItem))) return;
        var task = e.Data.GetData(typeof(TaskItem)) as TaskItem;
        if (task == null) return;

        var pos = e.GetPosition(listBox);
        var element = listBox.InputHitTest(pos) as DependencyObject;
        while (element != null && element is not ListBoxItem)
            element = VisualTreeHelper.GetParent(element);

        if (element is ListBoxItem lbi && lbi.DataContext is TaskList targetList
            && targetList.Id != task.ListId)
        {
            ViewModel.MoveTaskToListCommand.Execute((task, targetList));
        }
        e.Handled = true;
    }

    // ─── Drag & Drop ──────────────────────────────────────
    private void TaskRow_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe
            && fe.DataContext is TaskItem task && !task.IsClosed)
        {
            DragDrop.DoDragDrop(fe, task, DragDropEffects.Move);
        }
    }

    // Task row reorder drop handlers
    private void TaskRow_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskItem)) && sender is Border border)
        {
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
            border.BorderThickness = new Thickness(0, 0, 0, 2);
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
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
        _lastDropTime = DateTime.Now;
        if (sender is Border border && border.DataContext is TaskItem targetTask
            && e.Data.GetDataPresent(typeof(TaskItem)))
        {
            border.BorderBrush = new SolidColorBrush(Colors.Transparent);
            border.BorderThickness = new Thickness(0);

            var draggedTask = e.Data.GetData(typeof(TaskItem)) as TaskItem;
            if (draggedTask == null || draggedTask.Id == targetTask.Id) return;

            // Same list and group
            if (draggedTask.ListId != targetTask.ListId) return;
            if (draggedTask.GroupId != targetTask.GroupId) return;

            // Move dragged task to target's position
            var siblings = ViewModel.Tasks
                .Where(t => t.ListId == targetTask.ListId
                    && t.GroupId == targetTask.GroupId
                    && !t.IsClosed)
                .OrderBy(t => t.Order)
                .ToList();

            siblings.Remove(draggedTask);
            var targetIdx = siblings.IndexOf(targetTask);
            if (targetIdx < 0) return;

            siblings.Insert(targetIdx, draggedTask);

            // Reassign orders
            for (int i = 0; i < siblings.Count; i++)
                siblings[i].Order = i;

            // Save all
            foreach (var t in siblings)
                App.Database!.Tasks.Update(t);

            ViewModel.Refresh();
        }
        e.Handled = true;
    }

    private void GroupHeader_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskItem)) && sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF2, 0xFC)); // accent blue light
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }

    private void GroupHeader_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
        }
        e.Handled = true;
    }

    private void GroupHeader_Drop(object sender, DragEventArgs e)
    {
        _lastDropTime = DateTime.Now;
        if (sender is Border border)
        {
            border.Background = Brushes.Transparent;
            if (e.Data.GetDataPresent(typeof(TaskItem)) && border.DataContext is GroupedTasks gt && gt.HasGroup)
            {
                var task = e.Data.GetData(typeof(TaskItem)) as TaskItem;
                if (task != null && task.GroupId != gt.Group!.Id)
                {
                    ViewModel.MoveTaskToGroupCommand.Execute((task, gt.Group));
                }
            }
        }
        e.Handled = true;
    }

    // Section-level drop (for both grouped and ungrouped areas)
    private void GroupSection_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(typeof(TaskItem)) && sender is Border border)
        {
            border.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0xF2, 0xFC));
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
        _lastDropTime = DateTime.Now;
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
    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if ((DateTime.Now - _lastDropTime).TotalMilliseconds < 500) return;
        if (sender is Border border && border.DataContext is TaskItem task)
        {
            ViewModel.SelectedTask = task;
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
                if (MessageBox.Show(Loc.ConfirmDeleteMsg(task.Title), Loc.ConfirmDelete,
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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

    private void DetailPane_Delete(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        if (MessageBox.Show(Loc.ConfirmDeleteMsg(ViewModel.SelectedTask.Title), Loc.ConfirmDelete,
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilMonday == 0) daysUntilMonday = 7;
        var nextMonday = today.AddDays(daysUntilMonday);
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

    private void DetailDueDate_Clear(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.DueDate = null;
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
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

        // Due date label
        if (task.DueDate != null)
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(task.DueDate.Value).LocalDateTime;
            DueDateLabel.Text = dt.Date == DateTime.Today ? Loc.Today
                : dt.Date == DateTime.Today.AddDays(1) ? Loc.Tomorrow
                : dt.ToString("MMM d");
        }
        else
        {
            DueDateLabel.Text = "";
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
}
