namespace ToDo.Sync;

/// <summary>Server → client reply. <see cref="ServerSeq"/> is the new high-water mark;
/// <see cref="Changes"/> are all changes newer than the requested cursor, including the
/// ones just accepted plus any written by other devices in the meantime.</summary>
public class SyncResponse
{
    public long ServerSeq { get; set; }
    public List<SyncChange>? Changes { get; set; }

    /// <summary>Wire-protocol version the server is speaking. The client refuses to apply
    /// a reply when this differs from <see cref="SyncProtocol.Version"/>, surfacing a
    /// "server out of date" status instead of corrupting local data.</summary>
    public int ProtocolVersion { get; set; }
}
