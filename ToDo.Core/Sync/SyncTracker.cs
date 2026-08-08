using LiteDB;

namespace ToDo.Sync;

/// <summary>Latest-state-per-entity outbox. Every write upserts the entity's change;
/// the sync engine flushes <see cref="AllPending"/>, then <see cref="ClearPushed"/>
/// removes only what was actually sent (leaving any writes that landed mid-round-trip).</summary>
public class SyncTracker
{
    private readonly ILiteCollection<SyncEvent> _events;

    public SyncTracker(ILiteCollection<SyncEvent> events) => _events = events;

    /// <summary>When false, writes are not recorded (test/edge-case seam).</summary>
    public bool TrackingEnabled { get; set; } = true;

    public void Record(SyncChange? change)
    {
        if (change == null || !TrackingEnabled) return;
        var id = $"{change.Type}:{change.Id}";
        var existing = _events.FindById(id);
        var modifiedAt = Math.Max(change.ModifiedAt, existing?.ModifiedAt ?? 0);
        _events.Upsert(new SyncEvent
        {
            Id = id,
            EntityType = change.Type,
            EntityId = change.Id,
            ModifiedAt = modifiedAt,
            Deleted = change.Deleted,
            PayloadJson = change.Payload,
        });
    }

    public IEnumerable<SyncEvent> AllPending() => _events.FindAll();

    /// <summary>Removes the pushed events, keeping any whose outbox entry was rewritten
    /// during the round-trip (its ModifiedAt is newer than the pushed snapshot).</summary>
    public void ClearPushed(IEnumerable<SyncEvent> pushed)
    {
        if (!TrackingEnabled) return;
        var pushedById = pushed.ToDictionary(e => e.Id, e => e.ModifiedAt);
        foreach (var (id, pushedAt) in pushedById)
        {
            var current = _events.FindById(id);
            if (current != null && current.ModifiedAt <= pushedAt)
                _events.Delete(id);
        }
    }

    public void Clear() => _events.DeleteAll();
}
