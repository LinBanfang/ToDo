namespace ToDo.Plugin.Abstractions;

/// <summary>任务的只读快照。宿主在 UI 线程投影后交给插件，切断活对象引用。</summary>
public sealed record TaskDto
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Note { get; init; }
    public string ListId { get; init; } = "";
    public string? GroupId { get; init; }
    public int Order { get; init; }
    public bool IsImportant { get; init; }
    public bool IsMyDay { get; init; }
    public int MyDayOrder { get; init; }
    public long? DueDate { get; init; }        // Unix 毫秒（墙钟）
    public long? Reminder { get; init; }       // Unix 毫秒（墙钟）
    public long? FiredReminder { get; init; }
    public string[] TagIds { get; init; } = Array.Empty<string>();
    public TaskStepDto[] Steps { get; init; } = Array.Empty<TaskStepDto>();
    public bool Completed { get; init; }
    public string? CloseMode { get; init; }    // "Complete" / "Cancel" / null
    public long? ClosedAt { get; init; }       // Unix 毫秒（墙钟）
    public long CreatedAt { get; init; }
    /// <summary>⚠ HLC 编码（ADR-018），不是墙钟时间，勿当时间显示/比较语义。</summary>
    public long ModifiedAt { get; init; }
}

public sealed record TaskStepDto(string Id, string Title, bool Completed, int Order);

public sealed record TaskListDto
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Icon { get; init; } = "";
    public string Type { get; init; } = "";    // MyDay / Important / Planned / Tasks / Custom
    public bool IsSystem { get; init; }
    public string? GroupId { get; init; }
    public int Order { get; init; }
}

public sealed record TagDto(string Id, string Name, string Color);

/// <summary>新建任务草稿。ListId 默认收件箱（list-tasks）。</summary>
public sealed class NewTaskDraft
{
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public string ListId { get; set; } = "list-tasks";
    public string? GroupId { get; set; }
    public long? DueDate { get; set; }         // Unix 毫秒
    public bool IsImportant { get; set; }
    public string[] TagIds { get; set; } = Array.Empty<string>();
}
