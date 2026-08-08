namespace ToDo.Sync;

/// <summary>Server → client reply. <see cref="ServerSeq"/> is the new high-water mark;
/// <see cref="Changes"/> are all changes newer than the requested cursor, including the
/// ones just accepted plus any written by other devices in the meantime.</summary>
public class SyncResponse
{
    public long ServerSeq { get; set; }
    public List<SyncChange>? Changes { get; set; }
}
