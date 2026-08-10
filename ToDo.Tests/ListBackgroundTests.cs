using System;
using System.IO;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises the local-only per-list background image store (ADR-014): the untracked
/// list_backgrounds collection is Upserted by list id (single row per list), so sync's
/// whole-entity list upsert and in-app theme edits can never corrupt or duplicate bytes.
/// </summary>
public sealed class ListBackgroundTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-bg-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;

    public ListBackgroundTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SetAndGet_ReturnsBytesAndFileName()
    {
        _db.SetListBackground("lst", new byte[] { 1, 2, 3 }, "bg.png");

        Assert.Equal(new byte[] { 1, 2, 3 }, _db.GetListBackgroundData("lst"));
        Assert.Equal("bg.png", _db.GetListBackgroundFileName("lst"));
    }

    [Fact]
    public void NeverSet_ReturnsNull()
    {
        Assert.Null(_db.GetListBackgroundData("nope"));
        Assert.Null(_db.GetListBackgroundFileName("nope"));
    }

    [Fact]
    public void SetTwice_KeepsSingleRow()
    {
        // Upsert by _id = listId: the second write overwrites, it does not append.
        _db.SetListBackground("lst", new byte[] { 1 }, "one.png");
        _db.SetListBackground("lst", new byte[] { 9, 9 }, "two.png");

        Assert.Equal(new byte[] { 9, 9 }, _db.GetListBackgroundData("lst"));
        Assert.Equal("two.png", _db.GetListBackgroundFileName("lst"));
    }

    [Fact]
    public void Delete_RemovesBytes()
    {
        _db.SetListBackground("lst", new byte[] { 1, 2, 3 }, "bg.png");

        _db.DeleteListBackground("lst");

        Assert.Null(_db.GetListBackgroundData("lst"));
        Assert.Null(_db.GetListBackgroundFileName("lst"));
    }
}
