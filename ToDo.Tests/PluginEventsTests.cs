using System.IO;
using System.Windows.Threading;
using ToDo.Models;
using ToDo.Plugin.Abstractions;
using ToDo.Plugins;
using ToDo.Services;
using ToDo.ViewModels;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// M2 测试：事件总线在 VM 命令缝触发，以及门面写方法转发到命令缝（自动 HLC 盖章 / outbox / 刷新 / Raise）。
/// </summary>
[Collection("settings-shared")]
public sealed class PluginEventsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-pluginevents-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly TodoEvents _events = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly TodoHost _host;

    public PluginEventsTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
        SettingsService.UseDirectory(_dir);
        _vm = new MainViewModel(_db, events: _events);
        _host = new TodoHost(_db, _vm, _dispatcher, _events, "test.plugin", _ => { });
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ─── VM 命令缝的事件 ─────────────────────────────────────

    [Fact]
    public void CreateTask_raises_TaskCreated()
    {
        TaskDto? created = null;
        _events.TaskCreated += t => created = t;

        _vm.CreateTaskCommand.Execute("新任务");

        Assert.NotNull(created);
        Assert.Equal("新任务", created!.Title);
        Assert.Equal("list-tasks", created.ListId);
    }

    [Fact]
    public void CompleteTask_raises_TaskCompleted()
    {
        var task = _vm.CreateTaskFromDraft(new NewTaskDraft { Title = "要完成" });
        TaskDto? completed = null;
        _events.TaskCompleted += t => completed = t;

        _vm.CloseTaskCommand.Execute((task, CloseMode.Complete, false));

        Assert.NotNull(completed);
        Assert.Equal("要完成", completed!.Title);
        Assert.True(completed.Completed);
    }

    [Fact]
    public void UpdateTask_raises_TaskChanged()
    {
        var task = _vm.CreateTaskFromDraft(new NewTaskDraft { Title = "原标题" });
        TaskDto? changed = null;
        _events.TaskChanged += t => changed = t;

        task.Title = "新标题";
        _vm.UpdateTaskCommand.Execute(task);

        Assert.NotNull(changed);
        Assert.Equal("新标题", changed!.Title);
    }

    [Fact]
    public void Delete_then_Undo_raises_TaskDeleted_and_TaskRestored()
    {
        var task = _vm.CreateTaskFromDraft(new NewTaskDraft { Title = "要删" });
        string? deletedId = null;
        TaskDto? restored = null;
        _events.TaskDeleted += id => deletedId = id;
        _events.TaskRestored += t => restored = t;

        _vm.DeleteTaskCommand.Execute(task);
        Assert.Equal(task.Id, deletedId);

        _vm.UndoCommand.Execute(null);
        Assert.NotNull(restored);
        Assert.Equal("要删", restored!.Title);
    }

    // ─── 门面写方法 ─────────────────────────────────────────

    [Fact]
    public void Host_CreateTask_returns_dto_and_raises()
    {
        TaskDto? created = null;
        _events.TaskCreated += t => created = t;

        var dto = _host.CreateTask(new NewTaskDraft
        {
            Title = "导入任务",
            Note = "备注",
            DueDate = 1234567890000,
            IsImportant = true,
        });

        Assert.Equal("导入任务", dto.Title);
        Assert.Equal("备注", dto.Note);
        Assert.Equal(1234567890000, dto.DueDate);
        Assert.True(dto.IsImportant);
        Assert.NotNull(created);
        Assert.Equal(dto.Id, created!.Id);
    }

    [Fact]
    public void Host_UpdateTitle_AddStep_Tags_roundtrip()
    {
        var dto = _host.CreateTask(new NewTaskDraft { Title = "原标题" });

        _host.UpdateTaskTitle(dto.Id, "新标题");
        _host.AddTaskStep(dto.Id, "步骤一");

        var tag = _host.CreateTag("工作", "#0078D4");
        _host.AddTaskTag(dto.Id, tag.Id);

        var after = _host.GetTask(dto.Id)!;
        Assert.Equal("新标题", after.Title);
        Assert.Single(after.Steps);
        Assert.Equal("步骤一", after.Steps[0].Title);
        Assert.Contains(tag.Id, after.TagIds);
    }

    [Fact]
    public void Host_CompleteTask_completes()
    {
        var dto = _host.CreateTask(new NewTaskDraft { Title = "完成我" });
        TaskDto? completed = null;
        _events.TaskCompleted += t => completed = t;

        _host.CompleteTask(dto.Id);

        Assert.NotNull(completed);
        Assert.Equal(dto.Id, completed!.Id);
        Assert.True(completed.Completed);
        Assert.Equal("Complete", completed.CloseMode);
    }
}
