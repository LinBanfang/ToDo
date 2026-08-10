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

/// <summary>Per-list background theme. The type + color sync (custom lists only); the
/// image bytes themselves are local-only (ADR-014), stored in a separate untracked
/// collection so the sync layer's whole-entity LWW overwrite never touches them.</summary>
public enum ListBackgroundType
{
    None,
    Solid,
    Image
}

public partial class TaskList : ObservableObject, IOrdered
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = "";

    /// <summary>Background theme kind (None/Solid/Image). Synced for custom lists.</summary>
    [ObservableProperty]
    private ListBackgroundType _backgroundType;

    /// <summary>Solid background color as "#RRGGBB"; empty when none. Synced for custom lists.</summary>
    [ObservableProperty]
    private string _backgroundColor = "";

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
