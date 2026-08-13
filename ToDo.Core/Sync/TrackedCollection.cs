using System.Linq;
using System.Linq.Expressions;
using LiteDB;

namespace ToDo.Sync;

/// <summary>
/// Wraps an <see cref="ILiteCollection{T}"/> so every write stamps ModifiedAt and
/// records the entity in the sync outbox; all non-mutating members forward to the
/// inner collection. This is the single choke point that catches every write path —
/// the MainViewModel commands AND the direct MainWindow.xaml.cs bypasses — so no
/// mutation can slip past change tracking.
/// </summary>
public class TrackedCollection<T> : ILiteCollection<T> where T : class
{
    private readonly ILiteCollection<T> _inner;
    private readonly SyncTracker _tracker;
    private readonly string _entityType;
    private readonly Func<T, string> _getId;
    private readonly Action<T> _stamp;
    private readonly Func<T, bool>? _skip;
    private readonly Func<long>? _clockNow;

    public TrackedCollection(ILiteCollection<T> inner, SyncTracker tracker, string entityType,
        Func<T, string> getId, Action<T> stamp, Func<T, bool>? skip = null, Func<long>? clockNow = null)
    {
        _inner = inner;
        _tracker = tracker;
        _entityType = entityType;
        _getId = getId;
        _stamp = stamp;
        _skip = skip;
        _clockNow = clockNow;
    }

    private long Now() => _clockNow?.Invoke() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Record(T entity)
    {
        if (_skip?.Invoke(entity) == true) return;
        _tracker.Record(SyncEntitySerializer.ToChange(entity));
    }

    private void RecordTombstone(string id) =>
        _tracker.Record(new SyncChange { Type = _entityType, Id = id, ModifiedAt = Now(), Deleted = true });

    // ─── Intercepted mutations ─────────────────────────────────
    // Every path that can create/alter/delete entities passes through one of these.

    public BsonValue Insert(T entity)
    {
        _stamp(entity);
        var result = _inner.Insert(entity);
        Record(entity);
        return result;
    }

    public void Insert(BsonValue id, T entity)
    {
        _stamp(entity);
        _inner.Insert(id, entity);
        Record(entity);
    }

    public int Insert(IEnumerable<T> entities)
    {
        var list = entities is IReadOnlyList<T> r ? r : entities.ToList();  // materialize once
        foreach (var e in list) _stamp(e);
        var result = _inner.Insert(list);
        foreach (var e in list) Record(e);
        return result;
    }

    public int InsertBulk(IEnumerable<T> entities, int batchSize = 5000)
    {
        var list = entities is IReadOnlyList<T> r ? r : entities.ToList();
        foreach (var e in list) _stamp(e);
        var result = _inner.InsertBulk(list, batchSize);
        foreach (var e in list) Record(e);
        return result;
    }

    public bool Update(T entity)
    {
        _stamp(entity);
        var ok = _inner.Update(entity);
        if (ok) Record(entity);
        return ok;
    }

    public bool Update(BsonValue id, T entity)
    {
        _stamp(entity);
        var ok = _inner.Update(id, entity);
        if (ok) Record(entity);
        return ok;
    }

    public int Update(IEnumerable<T> entities)
    {
        var list = entities is IReadOnlyList<T> r ? r : entities.ToList();
        foreach (var e in list) _stamp(e);
        var n = _inner.Update(list);
        foreach (var e in list) Record(e);
        return n;
    }

    public bool Upsert(T entity)
    {
        _stamp(entity);
        var ok = _inner.Upsert(entity);
        Record(entity);
        return ok;
    }

    public bool Upsert(BsonValue id, T entity)
    {
        _stamp(entity);
        var ok = _inner.Upsert(id, entity);
        Record(entity);
        return ok;
    }

    public int Upsert(IEnumerable<T> entities)
    {
        var list = entities is IReadOnlyList<T> r ? r : entities.ToList();
        foreach (var e in list) _stamp(e);
        var result = _inner.Upsert(list);
        foreach (var e in list) Record(e);
        return result;
    }

    public bool Delete(BsonValue id)
    {
        var existed = _inner.FindById(id) != null;
        var ok = _inner.Delete(id);
        if (ok && existed) RecordTombstone(id.AsString);
        return ok;
    }

    // Convenience overload (entity ids are strings in this app).
    public bool Delete(string id)
    {
        var existed = _inner.FindById(id) != null;
        var ok = _inner.Delete(id);
        if (ok && existed) RecordTombstone(id);
        return ok;
    }

    public int DeleteAll()
    {
        var ids = _inner.FindAll().Select(_getId).ToList();
        var n = _inner.DeleteAll();
        foreach (var id in ids) RecordTombstone(id);
        return n;
    }

    public int DeleteMany(BsonExpression predicate)
    {
        var ids = _inner.Find(predicate).Select(_getId).ToList();
        var n = _inner.DeleteMany(predicate);
        foreach (var id in ids) RecordTombstone(id);
        return n;
    }

    public int DeleteMany(Expression<Func<T, bool>> predicate)
    {
        var ids = _inner.Find(predicate).Select(_getId).ToList();
        var n = _inner.DeleteMany(predicate);
        foreach (var id in ids) RecordTombstone(id);
        return n;
    }

    // String-predicate bulk deletes: no Find equivalent exists to enumerate the ids
    // up front, and the app never uses them, so they forward untracked (no tombstones).
    public int DeleteMany(string predicate, BsonDocument parameters) => _inner.DeleteMany(predicate, parameters);
    public int DeleteMany(string predicate, BsonValue[] args) => _inner.DeleteMany(predicate, args);

    // UpdateMany rewrites rows via expressions without handing us T instances;
    // unused by the app today, so forwarded untracked (documented limitation).
    public int UpdateMany(BsonExpression transform, BsonExpression predicate) => _inner.UpdateMany(transform, predicate);
    public int UpdateMany(Expression<Func<T, T>> extend, Expression<Func<T, bool>> predicate) => _inner.UpdateMany(extend, predicate);

    // ─── Forwards ──────────────────────────────────────────────

    public string Name => _inner.Name;
    public BsonAutoId AutoId => _inner.AutoId;
    public EntityMapper EntityMapper => _inner.EntityMapper;

    public ILiteCollection<T> Include(BsonExpression keySelector) => _inner.Include(keySelector);
    public ILiteCollection<T> Include<K>(Expression<Func<T, K>> keySelector) => _inner.Include(keySelector);
    public ILiteQueryable<T> Query() => _inner.Query();

    public IEnumerable<T> Find(BsonExpression predicate, int skip = 0, int limit = int.MaxValue) => _inner.Find(predicate, skip, limit);
    public IEnumerable<T> Find(Query query, int skip = 0, int limit = int.MaxValue) => _inner.Find(query, skip, limit);
    public IEnumerable<T> Find(Expression<Func<T, bool>> predicate, int skip = 0, int limit = int.MaxValue) => _inner.Find(predicate, skip, limit);
    public IEnumerable<T> FindAll() => _inner.FindAll();
    public T FindById(BsonValue id) => _inner.FindById(id);

    public T FindOne(BsonExpression predicate, BsonValue[] args) => _inner.FindOne(predicate, args);
    public T FindOne(BsonExpression predicate) => _inner.FindOne(predicate);
    public T FindOne(Query query) => _inner.FindOne(query);
    public T FindOne(Expression<Func<T, bool>> predicate) => _inner.FindOne(predicate);
    public T FindOne(string predicate, BsonDocument parameters) => _inner.FindOne(predicate, parameters);

    public bool Exists(BsonExpression predicate) => _inner.Exists(predicate);
    public bool Exists(Query query) => _inner.Exists(query);
    public bool Exists(Expression<Func<T, bool>> predicate) => _inner.Exists(predicate);
    public bool Exists(string predicate, BsonDocument parameters) => _inner.Exists(predicate, parameters);
    public bool Exists(string predicate, BsonValue[] args) => _inner.Exists(predicate, args);

    public int Count() => _inner.Count();
    public int Count(BsonExpression predicate) => _inner.Count(predicate);
    public int Count(Query query) => _inner.Count(query);
    public int Count(Expression<Func<T, bool>> predicate) => _inner.Count(predicate);
    public int Count(string predicate, BsonDocument parameters) => _inner.Count(predicate, parameters);
    public int Count(string predicate, BsonValue[] args) => _inner.Count(predicate, args);

    public long LongCount() => _inner.LongCount();
    public long LongCount(BsonExpression predicate) => _inner.LongCount(predicate);
    public long LongCount(Query query) => _inner.LongCount(query);
    public long LongCount(Expression<Func<T, bool>> predicate) => _inner.LongCount(predicate);
    public long LongCount(string predicate, BsonDocument parameters) => _inner.LongCount(predicate, parameters);
    public long LongCount(string predicate, BsonValue[] args) => _inner.LongCount(predicate, args);

    public K Max<K>(Expression<Func<T, K>> keySelector) => _inner.Max(keySelector);
    public BsonValue Max() => _inner.Max();
    public BsonValue Max(BsonExpression keySelector) => _inner.Max(keySelector);

    public K Min<K>(Expression<Func<T, K>> keySelector) => _inner.Min(keySelector);
    public BsonValue Min() => _inner.Min();
    public BsonValue Min(BsonExpression keySelector) => _inner.Min(keySelector);

    public bool EnsureIndex(BsonExpression expression, bool unique = false) => _inner.EnsureIndex(expression, unique);
    public bool EnsureIndex(string name, BsonExpression expression, bool unique = false) => _inner.EnsureIndex(name, expression, unique);
    public bool EnsureIndex<K>(Expression<Func<T, K>> keySelector, bool unique = false) => _inner.EnsureIndex(keySelector, unique);
    public bool EnsureIndex<K>(string name, Expression<Func<T, K>> keySelector, bool unique = false) => _inner.EnsureIndex(name, keySelector, unique);
    public bool DropIndex(string name) => _inner.DropIndex(name);
}
