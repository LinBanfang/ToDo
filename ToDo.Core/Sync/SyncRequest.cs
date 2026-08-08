namespace ToDo.Sync;

/// <summary>Client → server push payload. <see cref="Since"/> is the last ServerSeq this
/// device has applied, so the server can return only newer changes in the response.</summary>
public class SyncRequest
{
    public string DeviceId { get; set; } = "";
    public long Since { get; set; }
    public List<SyncChange>? Changes { get; set; }
}
