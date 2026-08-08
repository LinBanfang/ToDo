namespace ToDo.Sync;

/// <summary>Stable entity-type keys shared between the client outbox and the server.</summary>
public static class SyncEntityTypes
{
    public const string Task = "task";
    public const string List = "list";
    public const string Group = "group";
    public const string ListGroup = "listgroup";
    public const string Tag = "tag";
}

/// <summary>A single entity change: the wire unit exchanged between client and server.</summary>
public class SyncChange
{
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public long ModifiedAt { get; set; }
    public bool Deleted { get; set; }
    public string? Payload { get; set; }
}
