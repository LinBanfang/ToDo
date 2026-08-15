using ToDo.Models;
using ToDo.Plugin.Abstractions;

namespace ToDo.Plugins;

/// <summary>宿主模型 → 契约 DTO 快照映射。读门面（TodoHost）与事件总线（MainViewModel 的 Raise）共用，
/// 保证两者产出的 <see cref="TaskDto"/> 形状一致。</summary>
internal static class DtoMapper
{
    public static TaskDto ToTask(TaskItem t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Note = t.Note,
        ListId = t.ListId,
        GroupId = t.GroupId,
        Order = t.Order,
        IsImportant = t.IsImportant,
        IsMyDay = t.IsMyDay,
        MyDayOrder = t.MyDayOrder,
        DueDate = t.DueDate,
        Reminder = t.Reminder,
        FiredReminder = t.FiredReminder,
        TagIds = t.TagIds?.ToArray() ?? Array.Empty<string>(),
        Steps = t.Steps.Select(s => new TaskStepDto(s.Id, s.Title, s.Completed, s.Order)).ToArray(),
        Completed = t.Completed,
        CloseMode = t.CloseRecord?.CloseMode.ToString(),
        ClosedAt = t.CloseRecord?.ClosedAt,
        CreatedAt = t.CreatedAt,
        ModifiedAt = t.ModifiedAt,
    };

    public static TaskListDto ToList(TaskList l) => new()
    {
        Id = l.Id,
        Name = l.Name,
        Icon = l.Icon,
        Type = l.Type.ToString(),
        IsSystem = l.IsSystem,
        GroupId = l.GroupId,
        Order = l.Order,
    };

    public static TagDto ToTag(Tag t) => new(t.Id, t.Name, t.Color);
}
