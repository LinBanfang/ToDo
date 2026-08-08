using ToDo.Models;

namespace ToDo.Sync;

/// <summary>Wire snapshot of a TaskItem — deliberately excludes the per-device
/// IsMyDay/MyDayOrder fields (My Day stays local, like Microsoft To Do).</summary>
public class TaskSync
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public string ListId { get; set; } = "";
    public string? GroupId { get; set; }
    public int Order { get; set; }
    public bool IsImportant { get; set; }
    public long? DueDate { get; set; }
    public long? Reminder { get; set; }
    public List<string> TagIds { get; set; } = new();
    public List<TaskStepSync> Steps { get; set; } = new();
    public bool Completed { get; set; }
    public CloseRecordSync? CloseRecord { get; set; }
    public long CreatedAt { get; set; }
    public long ModifiedAt { get; set; }
}

public class TaskStepSync
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Completed { get; set; }
    public int Order { get; set; }
}

public class CloseRecordSync
{
    public long ClosedAt { get; set; }
    public CloseMode CloseMode { get; set; }
}

/// <summary>Wire snapshot of a TaskList — excludes the derived TaskCount.</summary>
public class TaskListSync
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public ListType Type { get; set; }
    public int Order { get; set; }
    public string? GroupId { get; set; }
    public bool IsSystem { get; set; }
    public long CreatedAt { get; set; }
    public long ModifiedAt { get; set; }
}

public class TaskGroupSync
{
    public string Id { get; set; } = "";
    public string ListId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Order { get; set; }
    public bool Collapsed { get; set; }
    public long ModifiedAt { get; set; }
}

public class ListGroupSync
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Order { get; set; }
    public bool Collapsed { get; set; }
    public long ModifiedAt { get; set; }
}

public class TagSync
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public long CreatedAt { get; set; }
    public long ModifiedAt { get; set; }
}
