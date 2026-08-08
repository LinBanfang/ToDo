using Microsoft.EntityFrameworkCore;
using ToDo.Sync;
using ToDo.Server.Models;

namespace ToDo.Server;

/// <summary>The sync protocol in one place: LWW-merge the pushed changes into the entity
/// store, then return everything newer than the caller's cursor. Pure merge logic,
/// unit-tested without HTTP.</summary>
public class SyncStore
{
    private readonly SyncDbContext _db;

    public SyncStore(SyncDbContext db) => _db = db;

    public SyncResult Merge(IEnumerable<SyncChange> pushed, long since)
    {
        // Serializable → Microsoft.Data.Sqlite issues BEGIN IMMEDIATE, taking the write
        // lock up front so the serverSeq counter can't be double-incremented by a
        // concurrent request (single-user server, but cheap to be correct).
        using var tx = _db.Database.BeginTransaction(System.Data.IsolationLevel.Serializable);
        try
        {
            var next = NextSeq();

            foreach (var change in pushed.OrderBy(c => c.ModifiedAt))
            {
                var existing = _db.SyncEntities.Find(change.Type, change.Id);

                // LWW: accept when there is no row, or when the push is not stale.
                // A stale push (older ModifiedAt) is dropped; the newer row is returned
                // to the pusher below, and its ApplySync overwrites the stale local copy.
                if (existing != null && change.ModifiedAt < existing.ModifiedAt) continue;

                var seq = next++;
                if (existing == null)
                {
                    _db.SyncEntities.Add(new SyncEntity
                    {
                        EntityType = change.Type,
                        EntityId = change.Id,
                        ModifiedAt = change.ModifiedAt,
                        Deleted = change.Deleted,
                        Payload = change.Payload,
                        ServerSeq = seq,
                    });
                }
                else
                {
                    existing.ModifiedAt = change.ModifiedAt;
                    existing.Deleted = change.Deleted;
                    existing.Payload = change.Payload;
                    existing.ServerSeq = seq;
                }
            }

            SaveSeq(next - 1);
            _db.SaveChanges();

            var changes = _db.SyncEntities
                .Where(e => e.ServerSeq > since)
                .OrderBy(e => e.ServerSeq)
                .Select(e => new SyncChange
                {
                    Type = e.EntityType,
                    Id = e.EntityId,
                    ModifiedAt = e.ModifiedAt,
                    Deleted = e.Deleted,
                    Payload = e.Payload,
                })
                .ToList();

            tx.Commit();
            return new SyncResult { ServerSeq = next - 1, Changes = changes };
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Next ServerSeq to allocate, seeded from the stored counter or, if it was
    /// ever lost, from the highest existing row seq.</summary>
    private long NextSeq()
    {
        var counter = _db.SyncMeta.Find("serverSeq");
        if (counter != null) return long.Parse(counter.Value) + 1;
        var max = _db.SyncEntities.Max(e => (long?)e.ServerSeq) ?? 0;
        return max + 1;
    }

    private void SaveSeq(long last)
    {
        var counter = _db.SyncMeta.Find("serverSeq");
        if (counter == null) _db.SyncMeta.Add(new SyncMeta { Key = "serverSeq", Value = last.ToString() });
        else counter.Value = last.ToString();
    }
}

public class SyncResult
{
    public long ServerSeq { get; set; }
    public List<SyncChange> Changes { get; set; } = new();
}
