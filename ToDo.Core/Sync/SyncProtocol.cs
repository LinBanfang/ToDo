namespace ToDo.Sync;

/// <summary>
/// Version of the sync wire protocol, shared by client and server (single source of
/// truth). Bump on any breaking change to <see cref="SyncRequest"/>/<see cref="SyncResponse"/>
/// or to the entity payloads; an old client pointed at a new server (or vice versa)
/// then fails loudly with a "version mismatch" status instead of corrupting data.
/// </summary>
public static class SyncProtocol
{
    public const int Version = 2;   // v2: ModifiedAt is an HLC-encoded long (ADR-018), not raw wall-clock ms
}
