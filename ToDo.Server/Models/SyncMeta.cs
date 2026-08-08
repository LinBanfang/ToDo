namespace ToDo.Server.Models;

/// <summary>Key/value store for server metadata. Currently holds "serverSeq": the last
/// ServerSeq handed out, so seqs are allocated monotonically across requests.</summary>
public class SyncMeta
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
