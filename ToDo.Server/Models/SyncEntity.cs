namespace ToDo.Server.Models;

/// <summary>The server's copy of one synced entity. <see cref="ServerSeq"/> is the
/// monotonic cursor that clients use for incremental pull (never wall-clock, so clock
/// skew and NTP jumps can't corrupt it). Deleted rows are retained as tombstones.</summary>
public class SyncEntity
{
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public long ModifiedAt { get; set; }
    public bool Deleted { get; set; }
    public string? Payload { get; set; }
    public long ServerSeq { get; set; }
}
