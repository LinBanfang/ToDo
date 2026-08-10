using System.Text.Json;
using System.Text.Json.Serialization;
using ToDo.Models;

namespace ToDo.Sync;

/// <summary>Maps entity objects to/from their wire DTOs (JSON payloads).
/// System lists are excluded from sync entirely — every device recreates them deterministically.</summary>
public static class SyncEntitySerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Builds the change for an entity, or null when it must not be synced.</summary>
    public static SyncChange? ToChange(object entity)
    {
        switch (entity)
        {
            case TaskItem t:
                return Change(SyncEntityTypes.Task, t.Id, t.ModifiedAt, new TaskSync
                {
                    Id = t.Id, Title = t.Title, Note = t.Note, ListId = t.ListId,
                    GroupId = t.GroupId, Order = t.Order, IsImportant = t.IsImportant,
                    DueDate = t.DueDate, Reminder = t.Reminder,
                    Recurrence = t.Recurrence, RecurrenceInterval = t.RecurrenceInterval, RecurrenceSeriesId = t.RecurrenceSeriesId,
                    TagIds = t.TagIds,
                    Steps = t.Steps.Select(s => new TaskStepSync { Id = s.Id, Title = s.Title, Completed = s.Completed, Order = s.Order }).ToList(),
                    Completed = t.Completed,
                    CloseRecord = t.CloseRecord == null
                        ? null
                        : new CloseRecordSync { ClosedAt = t.CloseRecord.ClosedAt, CloseMode = t.CloseRecord.CloseMode },
                    CreatedAt = t.CreatedAt, ModifiedAt = t.ModifiedAt,
                });
            case TaskList l when l.IsSystem:
                return null;   // system lists never sync
            case TaskList l:
                return Change(SyncEntityTypes.List, l.Id, l.ModifiedAt, new TaskListSync
                {
                    Id = l.Id, Name = l.Name, Icon = l.Icon, Type = l.Type,
                    Order = l.Order, GroupId = l.GroupId, IsSystem = false,
                    BackgroundType = l.BackgroundType,
                    BackgroundColor = string.IsNullOrEmpty(l.BackgroundColor) ? null : l.BackgroundColor,
                    CreatedAt = l.CreatedAt, ModifiedAt = l.ModifiedAt,
                });
            case TaskGroup g:
                return Change(SyncEntityTypes.Group, g.Id, g.ModifiedAt, new TaskGroupSync
                {
                    Id = g.Id, ListId = g.ListId, Name = g.Name, Order = g.Order,
                    Collapsed = g.Collapsed, ModifiedAt = g.ModifiedAt,
                });
            case ListGroup lg:
                return Change(SyncEntityTypes.ListGroup, lg.Id, lg.ModifiedAt, new ListGroupSync
                {
                    Id = lg.Id, Name = lg.Name, Order = lg.Order, Collapsed = lg.Collapsed, ModifiedAt = lg.ModifiedAt,
                });
            case Tag tag:
                return Change(SyncEntityTypes.Tag, tag.Id, tag.ModifiedAt, new TagSync
                {
                    Id = tag.Id, Name = tag.Name, Color = tag.Color, CreatedAt = tag.CreatedAt, ModifiedAt = tag.ModifiedAt,
                });
            default:
                return null;
        }
    }

    /// <summary>Rebuilds an entity instance from a non-deleted change, or null.</summary>
    public static object? FromChange(SyncChange change)
    {
        if (change.Deleted || string.IsNullOrEmpty(change.Payload)) return null;
        return change.Type switch
        {
            SyncEntityTypes.Task => FromTask(change.Payload),
            SyncEntityTypes.List => FromList(change.Payload),
            SyncEntityTypes.Group => FromGroup(change.Payload),
            SyncEntityTypes.ListGroup => FromListGroup(change.Payload),
            SyncEntityTypes.Tag => FromTag(change.Payload),
            _ => null,
        };
    }

    private static SyncChange Change(string type, string id, long modifiedAt, object dto) =>
        new() { Type = type, Id = id, ModifiedAt = modifiedAt, Deleted = false, Payload = JsonSerializer.Serialize(dto, dto.GetType(), JsonOptions) };

    private static TaskItem FromTask(string json)
    {
        var dto = JsonSerializer.Deserialize<TaskSync>(json, JsonOptions)!;
        return new TaskItem
        {
            Id = dto.Id, Title = dto.Title, Note = dto.Note, ListId = dto.ListId,
            GroupId = dto.GroupId, Order = dto.Order, IsImportant = dto.IsImportant,
            DueDate = dto.DueDate, Reminder = dto.Reminder,
            Recurrence = dto.Recurrence, RecurrenceInterval = dto.RecurrenceInterval, RecurrenceSeriesId = dto.RecurrenceSeriesId,
            TagIds = dto.TagIds,
            Steps = new System.Collections.ObjectModel.ObservableCollection<TaskStep>(
                dto.Steps.Select(s => new TaskStep { Id = s.Id, Title = s.Title, Completed = s.Completed, Order = s.Order })),
            Completed = dto.Completed,
            CloseRecord = dto.CloseRecord == null
                ? null
                : new CloseRecord { ClosedAt = dto.CloseRecord.ClosedAt, CloseMode = dto.CloseRecord.CloseMode },
            CreatedAt = dto.CreatedAt, ModifiedAt = dto.ModifiedAt,
        };
    }

    private static TaskList FromList(string json)
    {
        var dto = JsonSerializer.Deserialize<TaskListSync>(json, JsonOptions)!;
        return new TaskList
        {
            Id = dto.Id, Name = dto.Name, Icon = dto.Icon, Type = dto.Type,
            Order = dto.Order, GroupId = dto.GroupId, IsSystem = dto.IsSystem,
            BackgroundType = dto.BackgroundType,
            BackgroundColor = dto.BackgroundColor ?? "",
            CreatedAt = dto.CreatedAt, ModifiedAt = dto.ModifiedAt,
        };
    }

    private static TaskGroup FromGroup(string json)
    {
        var dto = JsonSerializer.Deserialize<TaskGroupSync>(json, JsonOptions)!;
        return new TaskGroup
        {
            Id = dto.Id, ListId = dto.ListId, Name = dto.Name, Order = dto.Order,
            Collapsed = dto.Collapsed, ModifiedAt = dto.ModifiedAt,
        };
    }

    private static ListGroup FromListGroup(string json)
    {
        var dto = JsonSerializer.Deserialize<ListGroupSync>(json, JsonOptions)!;
        return new ListGroup
        {
            Id = dto.Id, Name = dto.Name, Order = dto.Order, Collapsed = dto.Collapsed, ModifiedAt = dto.ModifiedAt,
        };
    }

    private static Tag FromTag(string json)
    {
        var dto = JsonSerializer.Deserialize<TagSync>(json, JsonOptions)!;
        return new Tag
        {
            Id = dto.Id, Name = dto.Name, Color = dto.Color, CreatedAt = dto.CreatedAt, ModifiedAt = dto.ModifiedAt,
        };
    }
}
