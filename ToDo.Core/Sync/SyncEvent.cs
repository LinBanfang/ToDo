using LiteDB;

namespace ToDo.Sync;

/// <summary>Outbox entry: the latest synced state of one entity (or its tombstone).</summary>
public class SyncEvent
{
    [BsonId]
    public string Id { get; set; } = "";   // "{entityType}:{entityId}"
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public long ModifiedAt { get; set; }
    public bool Deleted { get; set; }
    public string? PayloadJson { get; set; }
}
