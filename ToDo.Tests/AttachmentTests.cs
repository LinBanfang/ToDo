using System;
using System.IO;
using System.Linq;
using ToDo.Models;
using ToDo.Services;
using ToDo.Sync;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises the local-only attachment store (ADR-013): add / remove / cascade, the
/// guarantee that sync's whole-entity Task upsert can never wipe local attachments,
/// and the in-memory AttachmentCount refresh that drives the row paperclip icon.
/// </summary>
public sealed class AttachmentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-attach-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;

    public AttachmentTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static TaskAttachment Attach(string id, string taskId, string fileName, byte[] data) => new()
    {
        Id = id, TaskId = taskId, FileName = fileName, Data = data, Size = data.Length, AddedAt = 100,
    };

    private static SyncChange Change(object entity) => SyncEntitySerializer.ToChange(entity)!;

    private static SyncChange Tombstone(string type, string id, long modifiedAt) =>
        new() { Type = type, Id = id, ModifiedAt = modifiedAt, Deleted = true };

    [Fact]
    public void AddAndGet_ReturnsNewestFirst()
    {
        _db.AddAttachment(Attach("a1", "t1", "one.txt", new byte[] { 1 }));
        _db.AddAttachment(Attach("a2", "t1", "two.txt", new byte[] { 1, 2 }));
        _db.AddAttachment(Attach("a3", "t2", "other.txt", new byte[] { 3 }));

        Assert.Equal(2, _db.GetAttachmentCount("t1"));
        Assert.Equal(2, _db.GetAttachments("t1").Count);
        Assert.Single(_db.GetAttachments("t2"));
        Assert.Empty(_db.GetAttachments("t3"));
    }

    [Fact]
    public void DeleteAttachment_RemovesOnlyThatOne()
    {
        _db.AddAttachment(Attach("a1", "t1", "one.txt", new byte[] { 1 }));
        _db.AddAttachment(Attach("a2", "t1", "two.txt", new byte[] { 1, 2 }));

        _db.DeleteAttachment("a1");

        Assert.Equal(1, _db.GetAttachmentCount("t1"));
        Assert.Equal("two.txt", _db.GetAttachments("t1").Single().FileName);
    }

    [Fact]
    public void DeleteAttachmentsForTask_CascadesWithoutTouchingOthers()
    {
        _db.AddAttachment(Attach("a1", "t1", "one.txt", new byte[] { 1 }));
        _db.AddAttachment(Attach("a2", "t1", "two.txt", new byte[] { 1, 2 }));
        _db.AddAttachment(Attach("a3", "t2", "other.txt", new byte[] { 3 }));

        _db.DeleteAttachmentsForTask("t1");

        Assert.Equal(0, _db.GetAttachmentCount("t1"));
        Assert.Equal(1, _db.GetAttachmentCount("t2"));
    }

    [Fact]
    public void ApplySync_WholeEntityUpsert_NeverWipesLocalAttachments()
    {
        // Local task with an attachment, then a newer server copy of the SAME task arrives.
        _db.AddAttachment(Attach("a1", "t1", "report.pdf", new byte[] { 1, 2, 3 }));
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "from-server", ModifiedAt = 200 }) });

        // The attachment lives in a separate collection, untouched by the upsert.
        Assert.Equal("from-server", _db.Tasks.FindById("t1").Title);
        Assert.Equal(1, _db.GetAttachmentCount("t1"));
        Assert.Equal("report.pdf", _db.GetAttachments("t1").Single().FileName);
    }

    [Fact]
    public void TaskTombstone_DeletesLocalAttachments()
    {
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "a", ModifiedAt = 100 }) });
        _db.AddAttachment(Attach("a1", "t1", "report.pdf", new byte[] { 1, 2, 3 }));

        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.Task, "t1", 200) });

        Assert.Null(_db.Tasks.FindById("t1"));
        Assert.Equal(0, _db.GetAttachmentCount("t1"));   // no orphaned bytes
    }

    [Fact]
    public void StaleTombstone_KeepsTaskAndAttachments()
    {
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "a", ModifiedAt = 100 }) });
        _db.AddAttachment(Attach("a1", "t1", "report.pdf", new byte[] { 1, 2, 3 }));

        // Local edit bumps ModifiedAt past the incoming stale tombstone.
        _db.ApplySync(new[] { Change(new TaskItem { Id = "t1", ListId = "list-1", Title = "edited", ModifiedAt = 200 }) });
        _db.ApplySync(new[] { Tombstone(SyncEntityTypes.Task, "t1", 150) });

        Assert.NotNull(_db.Tasks.FindById("t1"));
        Assert.Equal(1, _db.GetAttachmentCount("t1"));
    }

    [Fact]
    public void RefreshAttachmentCounts_SetsInMemoryCounts()
    {
        _db.AddAttachment(Attach("a1", "t1", "one.txt", new byte[] { 1 }));
        _db.AddAttachment(Attach("a2", "t1", "two.txt", new byte[] { 1, 2 }));
        var t1 = new TaskItem { Id = "t1", ListId = "list-1" };
        var t2 = new TaskItem { Id = "t2", ListId = "list-1" };

        _db.RefreshAttachmentCounts(new[] { t1, t2 });

        Assert.Equal(2, t1.AttachmentCount);
        Assert.Equal(0, t2.AttachmentCount);
    }

    [Fact]
    public void SyncPayload_DoesNotSerializeAttachmentBytes()
    {
        var task = new TaskItem { Id = "t1", ListId = "list-1", Title = "a", ModifiedAt = 100 };
        var payload = SyncEntitySerializer.ToChange(task)!.Payload!;

        Assert.DoesNotContain("Attachment", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SizeDisplay_FormatsUnits()
    {
        Assert.Equal("100 B", new TaskAttachment { Size = 100 }.SizeDisplay);
        Assert.Equal("1.5 KB", new TaskAttachment { Size = 1536 }.SizeDisplay);
        Assert.Equal("2 MB", new TaskAttachment { Size = 2 * 1024 * 1024 }.SizeDisplay);
    }
}
