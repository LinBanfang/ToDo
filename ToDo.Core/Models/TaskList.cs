using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;

namespace ToDo.Models;

public enum ListType
{
    MyDay,
    Important,
    Planned,
    Tasks,
    Custom
}

public partial class TaskList : ObservableObject, IOrdered
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

    [ObservableProperty]
    private long _modifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Unclosed task count for this list (observable for real-time sidebar updates)
    /// </summary>
    [ObservableProperty]
    private int _taskCount;

    [ObservableProperty]
    [BsonIgnore]
    private bool _isRenaming;

    [ObservableProperty]
    [BsonIgnore]
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
