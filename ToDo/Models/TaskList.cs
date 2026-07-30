using CommunityToolkit.Mvvm.ComponentModel;

namespace ToDo.Models;

public enum ListType
{
    MyDay,
    Important,
    Planned,
    Tasks,
    Custom
}

public partial class TaskList : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = "";

    [ObservableProperty]
    private ListType _type = ListType.Custom;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private string? _groupId;

    [ObservableProperty]
    private bool _isSystem;

    [ObservableProperty]
    private long _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Unclosed task count for this list (observable for real-time sidebar updates)
    /// </summary>
    [ObservableProperty]
    private int _taskCount;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _editName = string.Empty;

    /// <summary>Localized display name for system lists</summary>
    public string DisplayName => IsSystem
        ? Id switch
        {
            "list-myday"     => Services.Loc.MyDay,
            "list-important" => Services.Loc.Important,
            "list-planned"   => Services.Loc.Planned,
            "list-tasks"     => Services.Loc.Tasks,
            _ => Name
        }
        : Name;
}
