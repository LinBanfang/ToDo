using LiteDB;
using ToDo.Models;
using ToDo.Sync;
using System.IO;

namespace ToDo.Services;

public class DatabaseService : IDisposable
{
    private readonly LiteDatabase _db;
    private readonly string _dbPath;
    private readonly SyncTracker _tracker;
    private readonly HybridClock? _clock;

    // Raw collections: ApplySync writes here directly, bypassing outbox tracking.
    private readonly ILiteCollection<TaskList> _rawLists;
    private readonly ILiteCollection<TaskGroup> _rawGroups;
    private readonly ILiteCollection<TaskItem> _rawTasks;
    private readonly ILiteCollection<Tag> _rawTags;
    private readonly ILiteCollection<ListGroup> _rawListGroups;
    // Attachments are local-only (ADR-013): a plain collection, deliberately NOT tracked —
    // no outbox, no sync payload, immune to ApplySync's whole-entity Task upsert.
    private readonly ILiteCollection<TaskAttachment> _rawAttachments;
    // List background images are local-only too (ADR-014), for the same reasons.
    private readonly ILiteCollection<ListBackground> _rawListBackgrounds;
    // List background opacity ("背景强弱") is local-only as well — a display preference
    // tied to a local-only asset, so a value that can't sync, in its own untracked collection.
    private readonly ILiteCollection<ListBackgroundSetting> _rawListBackgroundSettings;
    // Generic local-only KV (plugin settings/storage, ADR-020 D5): untracked, survives
    // backup/migration like attachments, namespaced by the plugin host facade.
    private readonly ILiteCollection<LocalKv> _rawLocalKv;

    public ILiteCollection<TaskList> Lists { get; }
    public ILiteCollection<TaskGroup> Groups { get; }
    public ILiteCollection<TaskItem> Tasks { get; }
    public ILiteCollection<Tag> Tags { get; }
    public ILiteCollection<ListGroup> ListGroups { get; }
    public ILiteCollection<TaskAttachment> Attachments { get; }

    public SyncTracker Tracker => _tracker;
    public string StoragePath => _dbPath;
    /// <summary>The HLC clock when one was supplied, else null (tests/legacy).</summary>
    public HybridClock? Clock => _clock;

    public DatabaseService(string dbPath, HybridClock? clock = null)
    {
        _dbPath = dbPath;
        _clock = clock;
        _db = new LiteDatabase($"Filename={_dbPath};Connection=direct");

        // Map ObservableCollection types to List for serialization
        var mapper = _db.Mapper;
        mapper.RegisterType<System.Collections.ObjectModel.ObservableCollection<TaskStep>>(
            serialize: (oc) =>
            {
                var list = new List<TaskStep>(oc);
                return mapper.Serialize(list);
            },
            deserialize: (bson) =>
            {
                var list = mapper.Deserialize<List<TaskStep>>(bson);
                return new System.Collections.ObjectModel.ObservableCollection<TaskStep>(list);
            }
        );

        _rawLists = _db.GetCollection<TaskList>("lists");
        _rawGroups = _db.GetCollection<TaskGroup>("groups");
        _rawTasks = _db.GetCollection<TaskItem>("tasks");
        _rawTags = _db.GetCollection<Tag>("tags");
        _rawListGroups = _db.GetCollection<ListGroup>("listgroups");
        _rawAttachments = _db.GetCollection<TaskAttachment>("attachments");
        _rawListBackgrounds = _db.GetCollection<ListBackground>("list_backgrounds");
        _rawListBackgroundSettings = _db.GetCollection<ListBackgroundSetting>("list_background_settings");
        _rawLocalKv = _db.GetCollection<LocalKv>("local_kv");

        _tracker = new SyncTracker(_db.GetCollection<SyncEvent>("sync_events"));

        // Tracked wrappers: every mutation stamps ModifiedAt and fills the outbox. When an
        // HLC clock is supplied (production), ModifiedAt is a hybrid-logical timestamp
        // (ADR-018); without one (tests/legacy) it falls back to raw wall-clock ms.
        Lists = new TrackedCollection<TaskList>(_rawLists, _tracker, SyncEntityTypes.List,
            l => l.Id, l => l.ModifiedAt = StampNow(), skip: l => l.IsSystem, clockNow: ClockTick);
        Groups = new TrackedCollection<TaskGroup>(_rawGroups, _tracker, SyncEntityTypes.Group,
            g => g.Id, g => g.ModifiedAt = StampNow(), clockNow: ClockTick);
        Tasks = new TrackedCollection<TaskItem>(_rawTasks, _tracker, SyncEntityTypes.Task,
            t => t.Id, t => t.ModifiedAt = StampNow(), clockNow: ClockTick);
        Tags = new TrackedCollection<Tag>(_rawTags, _tracker, SyncEntityTypes.Tag,
            t => t.Id, t => t.ModifiedAt = StampNow(), clockNow: ClockTick);
        ListGroups = new TrackedCollection<ListGroup>(_rawListGroups, _tracker, SyncEntityTypes.ListGroup,
            lg => lg.Id, lg => lg.ModifiedAt = StampNow(), clockNow: ClockTick);
        Attachments = _rawAttachments;

        // Ensure indexes
        Lists.EnsureIndex(x => x.Type);
        Lists.EnsureIndex(x => x.Order);
        Groups.EnsureIndex(x => x.ListId);
        Tasks.EnsureIndex(x => x.ListId);
        Tasks.EnsureIndex(x => x.GroupId);
        Tasks.EnsureIndex(x => x.IsMyDay);
        Tasks.EnsureIndex(x => x.IsImportant);
        Tasks.EnsureIndex(x => x.DueDate);
        Tasks.EnsureIndex(x => x.Reminder);
        ListGroups.EnsureIndex(x => x.Order);
        Tags.EnsureIndex(x => x.Name, unique: true);
        Attachments.EnsureIndex(x => x.TaskId);

        SeedDefaultData();
    }

    /// <summary>Raw wall-clock ms for non-sync fields (e.g. CreatedAt).</summary>
    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Stamps ModifiedAt for a write: HLC when a clock is present, raw wall clock
    /// otherwise (tests construct DatabaseService without a clock).</summary>
    private long StampNow() => _clock?.Tick() ?? Now();

    /// <summary>Clock-now delegate for tombstone stamping; null when no clock (then the
    /// tracked collection falls back to raw wall-clock ms).</summary>
    private Func<long>? ClockTick => _clock == null ? null : () => _clock.Tick();

    private void SeedDefaultData()
    {
        var now = Now();
        var systemDefaults = new Dictionary<string, (string Name, string Icon, ListType Type)>
        {
            ["list-myday"]     = ("My Day",    "☀️", ListType.MyDay),
            ["list-important"] = ("Important", "⭐",   ListType.Important),
            ["list-planned"]   = ("Planned",   "📅", ListType.Planned),
            ["list-tasks"]     = ("Tasks",     "🏠", ListType.Tasks),
        };

        if (Lists.Count() == 0)
        {
            var systemLists = systemDefaults.Select(kv => new TaskList
            {
                Id = kv.Key, Name = kv.Value.Name, Icon = kv.Value.Icon,
                Type = kv.Value.Type, IsSystem = true, Order = 0, CreatedAt = now
            }).ToArray();
            Lists.InsertBulk(systemLists);
        }
        else
        {
            // Migrate: ensure system lists have icons
            foreach (var (id, (name, icon, type)) in systemDefaults)
            {
                var existing = Lists.FindById(id);
                if (existing != null && string.IsNullOrEmpty(existing.Icon))
                {
                    existing.Icon = icon;
                    Lists.Update(existing);
                }
            }
        }
    }

    // ─── Sync entry points ────────────────────────────────────

    /// <summary>Seeds the outbox with the current state of every syncable entity so the
    /// first sync uploads pre-existing data. Call once when the device has never synced.</summary>
    public void BootstrapSync()
    {
        foreach (var l in _rawLists.FindAll()) RecordIfSyncable(l);
        foreach (var g in _rawGroups.FindAll()) RecordIfSyncable(g);
        foreach (var t in _rawTasks.FindAll()) RecordIfSyncable(t);
        foreach (var t in _rawTags.FindAll()) RecordIfSyncable(t);
        foreach (var lg in _rawListGroups.FindAll()) RecordIfSyncable(lg);
    }

    private void RecordIfSyncable(object entity) => _tracker.Record(SyncEntitySerializer.ToChange(entity));

    /// <summary>Raw wall-clock ms never exceeds this before year ~34000, while every HLC
    /// encoding (physical ≥ 2026) exceeds it — the boundary separating the two encodings.</summary>
    private const long HlcEpoch = 1L << 50;

    /// <summary>One-time migration to HLC timestamps (ADR-018 §迁移). Existing entities carry
    /// raw wall-clock-ms ModifiedAt; rebase each to (physical&lt;&lt;21 | 0 | 0), seed the clock's
    /// high-water mark to the max raw value, then re-seed the outbox so the rebased values
    /// re-push to the server. Idempotent: only values below <see cref="HlcEpoch"/> (raw ms)
    /// are rebased, so a re-run — or a fresh install — finds nothing to do.</summary>
    public void MigrateToHlc()
    {
        if (_clock == null) return;

        long max = 0;
        void Rebase<T>(ILiteCollection<T> col, Func<T, long> get, Action<T, long> set) where T : class
        {
            foreach (var e in col.FindAll())
            {
                var v = get(e);
                if (v <= 0 || v >= HlcEpoch) continue;   // unset or already HLC
                set(e, v << 21);
                col.Update(e);
                if (v > max) max = v;
            }
        }

        Rebase(_rawLists, l => l.ModifiedAt, (l, v) => l.ModifiedAt = v);
        Rebase(_rawGroups, g => g.ModifiedAt, (g, v) => g.ModifiedAt = v);
        Rebase(_rawTasks, t => t.ModifiedAt, (t, v) => t.ModifiedAt = v);
        Rebase(_rawTags, t => t.ModifiedAt, (t, v) => t.ModifiedAt = v);
        Rebase(_rawListGroups, lg => lg.ModifiedAt, (lg, v) => lg.ModifiedAt = v);

        if (max > 0)
        {
            _clock.Observe(max << 21);   // post-migration writes must sort after the newest rebased entity
            BootstrapSync();             // re-push rebased values (upsert keeps the larger ModifiedAt)
        }
    }

    /// <summary>
    /// Applies server changes to the raw collections, bypassing outbox tracking.
    /// Client-side LWW: a change is skipped when the local copy is newer. My Day
    /// (IsMyDay/MyDayOrder) is never overwritten — it stays per-device.
    /// </summary>
    public void ApplySync(IEnumerable<SyncChange> changes)
    {
        foreach (var change in changes.OrderBy(c => c.ModifiedAt))
        {
            try
            {
                _clock?.Observe(change.ModifiedAt);   // merge remote timestamp → next local write sorts after it
                ApplyOne(change);
            }
            catch (Exception ex) { SyncDiagnostics.Warn($"ApplySync {change.Type}:{change.Id} failed: {ex.Message}"); }
        }
    }

    /// <summary>Local-newer LWW check: returns true (and logs the conflict) when the local
    /// copy is newer than the incoming change, so the remote change is skipped.</summary>
    private static bool IsLocalNewer(long? localModifiedAt, SyncChange change)
    {
        if (localModifiedAt == null || localModifiedAt <= change.ModifiedAt) return false;
        SyncDiagnostics.Info($"ApplySync conflict: kept local {change.Type}:{change.Id} (local {localModifiedAt} > remote {change.ModifiedAt})");
        return true;
    }

    private void ApplyOne(SyncChange change)
    {
        if (change.Deleted)
        {
            ApplyTombstone(change);
            return;
        }
        if (string.IsNullOrEmpty(change.Payload))
        {
            SyncDiagnostics.Info($"ApplySync skipped {change.Type}:{change.Id} (empty payload)");
            return;
        }

        switch (change.Type)
        {
            case SyncEntityTypes.Task:
            {
                var incoming = (TaskItem)SyncEntitySerializer.FromChange(change)!;
                var local = _rawTasks.FindById(incoming.Id);
                if (IsLocalNewer(local?.ModifiedAt, change)) return;
                if (local != null) { incoming.IsMyDay = local.IsMyDay; incoming.MyDayOrder = local.MyDayOrder; }
                _rawTasks.Upsert(incoming);
                break;
            }
            case SyncEntityTypes.List:
            {
                var incoming = (TaskList)SyncEntitySerializer.FromChange(change)!;
                var local = _rawLists.FindById(incoming.Id);
                if (IsLocalNewer(local?.ModifiedAt, change)) return;
                _rawLists.Upsert(incoming);
                break;
            }
            case SyncEntityTypes.Group:
            {
                var incoming = (TaskGroup)SyncEntitySerializer.FromChange(change)!;
                var local = _rawGroups.FindById(incoming.Id);
                if (IsLocalNewer(local?.ModifiedAt, change)) return;
                _rawGroups.Upsert(incoming);
                break;
            }
            case SyncEntityTypes.ListGroup:
            {
                var incoming = (ListGroup)SyncEntitySerializer.FromChange(change)!;
                var local = _rawListGroups.FindById(incoming.Id);
                if (IsLocalNewer(local?.ModifiedAt, change)) return;
                _rawListGroups.Upsert(incoming);
                break;
            }
            case SyncEntityTypes.Tag:
            {
                var incoming = (Tag)SyncEntitySerializer.FromChange(change)!;
                var local = _rawTags.FindById(incoming.Id);
                if (IsLocalNewer(local?.ModifiedAt, change)) return;
                _rawTags.Upsert(incoming);
                break;
            }
        }
    }

    /// <summary>
    /// Applies a remote tombstone, mirroring the app's own delete cascades so remote
    /// deletes don't strand orphaned tasks. A local edit that is newer than the
    /// tombstone wins (the entity is kept and its newer state re-pushed later).
    /// </summary>
    private void ApplyTombstone(SyncChange change)
    {
        switch (change.Type)
        {
            case SyncEntityTypes.Task:
                var task = _rawTasks.FindById(change.Id);
                if (IsLocalNewer(task?.ModifiedAt, change)) return;
                _rawTasks.Delete(change.Id);
                DeleteAttachmentsForTask(change.Id);   // local attachments die with the task
                break;
            case SyncEntityTypes.List:
                var list = _rawLists.FindById(change.Id);
                if (IsLocalNewer(list?.ModifiedAt, change)) return;
                foreach (var t in _rawTasks.Find(t => t.ListId == change.Id))
                {
                    t.ListId = "list-tasks";   // orphaned tasks → inbox, like the app's DeleteList
                    t.GroupId = null;
                    _rawTasks.Update(t);
                }
                _rawGroups.DeleteMany(g => g.ListId == change.Id);
                _rawLists.Delete(change.Id);
                DeleteListBackground(change.Id);   // local background image dies with the list
                DeleteListBackgroundSetting(change.Id);   // ...and so do its display settings
                break;
            case SyncEntityTypes.Group:
                var group = _rawGroups.FindById(change.Id);
                if (IsLocalNewer(group?.ModifiedAt, change)) return;
                foreach (var t in _rawTasks.Find(t => t.GroupId == change.Id))
                {
                    t.GroupId = null;
                    _rawTasks.Update(t);
                }
                _rawGroups.Delete(change.Id);
                break;
            case SyncEntityTypes.ListGroup:
                var listGroup = _rawListGroups.FindById(change.Id);
                if (IsLocalNewer(listGroup?.ModifiedAt, change)) return;
                foreach (var l in _rawLists.Find(l => l.GroupId == change.Id))
                {
                    l.GroupId = null;
                    _rawLists.Update(l);
                }
                _rawListGroups.Delete(change.Id);
                break;
            case SyncEntityTypes.Tag:
                var tag = _rawTags.FindById(change.Id);
                if (IsLocalNewer(tag?.ModifiedAt, change)) return;
                foreach (var t in _rawTasks.FindAll())
                {
                    if (t.TagIds.Remove(change.Id)) _rawTasks.Update(t);
                }
                _rawTags.Delete(change.Id);
                break;
        }
    }

    /// <summary>Persists a task's per-device My Day state (IsMyDay / MyDayOrder) without
    /// stamping ModifiedAt or filling the sync outbox. My Day is local-only (the TaskSync
    /// DTO excludes it, ADR-015), so the daily reset must not rewrite the syncable
    /// ModifiedAt — doing so re-uploads a stale snapshot and lets a device with a newer
    /// wall-clock win an LWW conflict over a genuinely newer edit on another device.</summary>
    public void UpdateMyDayLocal(TaskItem task) => _rawTasks.Update(task);

    /// <summary>Flushes LiteDB's journal then copies the database file to <paramref name="destPath"/>.
    /// Attachments live inside the DB file (ADR-013), so a backup automatically includes them.</summary>
    public void ExportTo(string destPath)
    {
        _db.Checkpoint();
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        File.Copy(_dbPath, destPath, overwrite: true);
    }

    // ─── Local-only generic KV (plugin settings/storage, ADR-020 D5) ──

    /// <summary>Reads a local-only KV value, or null when absent.</summary>
    public string? GetLocalValue(string key) => _rawLocalKv.FindById(key)?.Value;

    /// <summary>Upserts a local-only KV value (unset when <paramref name="value"/> is null).</summary>
    public void SetLocalValue(string key, string? value)
    {
        if (value == null) { _rawLocalKv.Delete(key); return; }
        _rawLocalKv.Upsert(new LocalKv { Id = key, Value = value });
    }

    /// <summary>Deletes a local-only KV value.</summary>
    public void RemoveLocalValue(string key) => _rawLocalKv.Delete(key);

    /// <summary>All local-only KV keys under a prefix (e.g. "plugins/&lt;Id&gt;/").</summary>
    public IEnumerable<string> GetLocalKeys(string prefix) =>
        _rawLocalKv.Find(k => k.Id.StartsWith(prefix)).Select(k => k.Id);

    /// <summary>Deletes every local-only KV row whose key starts with <paramref name="prefix"/>
    /// (used to cascade-clean a removed plugin's data).</summary>
    public void RemoveLocalKeys(string prefix) =>
        _rawLocalKv.DeleteMany(k => k.Id.StartsWith(prefix));

    /// <summary>Total stored bytes under a prefix (key length + value length), for the
    /// per-plugin size cap (M4).</summary>
    public int GetLocalTotalBytes(string prefix) =>
        _rawLocalKv.Find(k => k.Id.StartsWith(prefix)).Sum(k => k.Id.Length + k.Value.Length);

    // ─── Attachments (local-only, ADR-013) ─────────────────

    /// <summary>Attachments of a task, newest first.</summary>
    public List<TaskAttachment> GetAttachments(string taskId) =>
        _rawAttachments.Find(a => a.TaskId == taskId).OrderByDescending(a => a.AddedAt).ToList();

    public int GetAttachmentCount(string taskId) => _rawAttachments.Count(a => a.TaskId == taskId);

    public void AddAttachment(TaskAttachment attachment) => _rawAttachments.Insert(attachment);

    public void DeleteAttachment(string id) => _rawAttachments.Delete(id);

    /// <summary>Deletes every attachment of a task. Called wherever a task is removed
    /// (the app's DeleteTask and ApplySync's task tombstone) so no orphan bytes remain.</summary>
    public void DeleteAttachmentsForTask(string taskId) => _rawAttachments.DeleteMany(a => a.TaskId == taskId);

    /// <summary>Sets each task's in-memory AttachmentCount (row paperclip indicator) from
    /// indexed counts. Loads no attachment bytes; called on task load and after add/remove.</summary>
    public void RefreshAttachmentCounts(IEnumerable<TaskItem> tasks)
    {
        foreach (var t in tasks)
            t.AttachmentCount = GetAttachmentCount(t.Id);
    }

    // ─── List backgrounds (local-only image bytes, ADR-014) ──

    /// <summary>Image bytes of a list's background, or null when it has none.</summary>
    public byte[]? GetListBackgroundData(string listId) => _rawListBackgrounds.FindById(listId)?.Data;

    /// <summary>Original file name of a list's background image, or null.</summary>
    public string? GetListBackgroundFileName(string listId) => _rawListBackgrounds.FindById(listId)?.FileName;

    /// <summary>Stores (or replaces) a list's background image. Upserts by _id = listId
    /// so each list keeps exactly one row.</summary>
    public void SetListBackground(string listId, byte[] data, string? fileName) =>
        _rawListBackgrounds.Upsert(new ListBackground
        {
            Id = listId,
            ListId = listId,
            Data = data,
            FileName = fileName ?? "",
        });

    /// <summary>Deletes a list's background image. Called wherever a list is removed
    /// (the app's DeleteList and ApplySync's list tombstone) so no orphan bytes remain.</summary>
    public void DeleteListBackground(string listId) => _rawListBackgrounds.Delete(listId);

    // ─── List theme display settings (background strength + card opacity, local-only per list, ADR-014) ──

    /// <summary>A list's display settings (background strength 20..100, card opacity 30..100,
    /// title text mode 0 auto / 1 dark / 2 light), or their defaults when the list has no row.
    /// All three share one row so a whole-entity list upsert from sync can never wipe one
    /// while updating the others.</summary>
    public (int Background, int Card, int TitleMode) GetListThemeSettings(string listId)
    {
        var row = _rawListBackgroundSettings.FindById(listId);
        if (row == null) return (100, 65, 0);
        return (row.OpacityPercent, row.CardOpacityPercent > 0 ? row.CardOpacityPercent : 65, row.TitleTextMode);
    }

    /// <summary>Stores all display settings in one row (Upsert by _id = listId). When every
    /// one is at its default the row is removed, so the collection only holds non-defaults
    /// and a missing row reads back as the defaults.</summary>
    public void SetListThemeSettings(string listId, int background, int card, int titleMode)
    {
        if (background == 100 && card == 65 && titleMode == 0) { _rawListBackgroundSettings.Delete(listId); return; }
        _rawListBackgroundSettings.Upsert(new ListBackgroundSetting
        {
            Id = listId,
            OpacityPercent = background,
            CardOpacityPercent = card,
            TitleTextMode = titleMode,
        });
    }

    /// <summary>Removes a list's display settings (back to the defaults). Called when a list
    /// is removed (the app's DeleteList and ApplySync's list tombstone) so no orphan rows
    /// remain.</summary>
    public void DeleteListBackgroundSetting(string listId) => _rawListBackgroundSettings.Delete(listId);

    public void Dispose()
    {
        _db?.Dispose();
    }
}
