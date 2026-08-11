using CommunityToolkit.Mvvm.Input;
using ToDo.Models;
using ToDo.Services;

namespace ToDo.ViewModels;

public partial class MainViewModel
{
    // ─── List Commands ────────────────────────────────────
    [RelayCommand]
    private void CreateListGroup(string name)
    {
        var g = new ListGroup { Name = name, Order = NextOrder(ListGroups.Select(x => x.Order)) };
        _db.ListGroups.Insert(g);
        LoadListGroups();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void RenameListGroup(ListGroup g)
    {
        _db.ListGroups.Update(g);
        LoadListGroups();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void DeleteListGroup(ListGroup g)
    {
        // Move lists in this group to ungrouped
        var listsInGroup = CustomLists.Where(l => l.GroupId == g.Id).ToList();
        foreach (var l in listsInGroup)
        {
            l.GroupId = null;
            _db.Lists.Update(l);
        }
        _db.ListGroups.Delete(g.Id);
        LoadListGroups();
        LoadLists();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void ToggleListGroupCollapse(ListGroup g)
    {
        g.Collapsed = !g.Collapsed;
        _db.ListGroups.Update(g);
        LoadListGroups();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void MoveListToGroup((TaskList list, ListGroup? group) param)
    {
        param.list.GroupId = param.group?.Id;
        _db.Lists.Update(param.list);
        // Auto-expand target group
        if (param.group != null && param.group.Collapsed)
        {
            param.group.Collapsed = false;
            _db.ListGroups.Update(param.group);
        }
        LoadListGroups();
        LoadLists();
        RebuildSidebarGroups();
    }

    [RelayCommand]
    private void CreateList(string name)
    {
        var list = new TaskList
        {
            Name = name,
            Icon = "📋",
            Type = ListType.Custom,
            Order = NextOrder(Lists.Where(l => !l.IsSystem).Select(l => l.Order)),
        };
        _db.Lists.Insert(list);
        LastCreatedListId = list.Id;
        LoadLists();
    }

    [RelayCommand]
    private void RenameList(TaskList list)
    {
        _db.Lists.Update(list);
        LoadLists();
    }

    /// <summary>Applies a list's background theme (called by the theme dialog's OK).
    /// The type + color sync via the tracked Lists collection; the image bytes and the
    /// display settings (background strength, card opacity) are local-only in untracked
    /// collections (ADR-014). Raises the background properties explicitly — LoadLists won't
    /// re-point an in-place-edited ActiveList.</summary>
    public void SetListTheme(TaskList list, ListBackgroundType type, string color,
                             byte[]? image, string? fileName, int opacityPercent = 100,
                             int cardOpacity = 65, int titleMode = 0)
    {
        list.BackgroundType = type;
        list.BackgroundColor = color;
        _db.Lists.Update(list);
        if (image != null) _db.SetListBackground(list.Id, image, fileName);
        else _db.DeleteListBackground(list.Id);
        // Display settings only earn a row when any of them differs from its default, so the
        // collection holds "non-default" settings and a missing row reads back as 100/65/auto.
        _db.SetListThemeSettings(list.Id, opacityPercent, cardOpacity, titleMode);
        // The shared card brushes reflect the ACTIVE list's opacity (ADR-014); if the dialog
        // edited that list apply the new value now, otherwise it lands when the list becomes
        // active (OnActiveListChanged re-tints).
        if (list.Id == ActiveList?.Id) ThemeService.SetCardOpacity(cardOpacity);
        OnPropertyChanged(nameof(ListBackgroundBrush));
        OnPropertyChanged(nameof(ListBackgroundMaskVisible));
        OnPropertyChanged(nameof(HeaderTitleLight));
    }

    [RelayCommand]
    private void DeleteList(TaskList list)
    {
        if (list.IsSystem) return;

        // Preserve data: move tasks to the Tasks (inbox) list instead of deleting them
        var movedTasks = _db.Tasks.Find(t => t.ListId == list.Id).ToList();
        foreach (var t in movedTasks)
        {
            t.ListId = "list-tasks";
            t.GroupId = null;
            t.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _db.Tasks.Update(t);
        }

        _db.Groups.DeleteMany(g => g.ListId == list.Id);
        _db.Lists.Delete(list.Id);
        _db.DeleteListBackground(list.Id);   // local background image dies with the list (ADR-014)
        _db.DeleteListBackgroundSetting(list.Id);   // ...and so does its opacity setting
        LoadAll();
        if (ActiveList?.Id == list.Id)
            ActiveList = Lists.FirstOrDefault(l => l.Id == "list-tasks");
    }

    // ─── Group Commands ───────────────────────────────────
    [RelayCommand]
    private void CreateGroup(string name)
    {
        if (ActiveList == null) return;
        var group = new TaskGroup
        {
            Name = name,
            ListId = ActiveList.Id,
            Order = NextOrder(Groups.Where(g => g.ListId == ActiveList.Id).Select(g => g.Order)),
        };
        _db.Groups.Insert(group);
        LoadGroups();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void RenameGroup(TaskGroup group)
    {
        _db.Groups.Update(group);
        LoadGroups();
    }

    [RelayCommand]
    private void DeleteGroup(TaskGroup group)
    {
        // Move tasks to ungrouped
        var tasksInGroup = Tasks.Where(t => t.GroupId == group.Id).ToList();
        foreach (var t in tasksInGroup)
        {
            t.GroupId = null;
            _db.Tasks.Update(t);
        }
        _db.Groups.Delete(group.Id);
        LoadGroups();
        RefreshActiveTasks();
    }

    [RelayCommand]
    private void ToggleGroupCollapse(TaskGroup group)
    {
        group.Collapsed = !group.Collapsed;
        _db.Groups.Update(group);
        LoadGroups();
        RefreshActiveTasks();
    }

}
