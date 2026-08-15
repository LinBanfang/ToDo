using System.IO;
using System.Windows.Threading;
using ToDo.Models;
using ToDo.Plugins;
using ToDo.Services;
using ToDo.ViewModels;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// M1 集成测试：验证「插件 DLL 放进 plugins\ 目录 → 加载 → 注册侧边栏入口 → 只读门面返回快照」
/// 整条管线。ExportPlugin 由项目引用带入测试输出目录，测试再把它 stage 到临时 plugins 目录。
/// </summary>
[Collection("settings-shared")]
public sealed class PluginManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-plugintests-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public PluginManagerTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
        SettingsService.UseDirectory(_dir);
        _vm = new MainViewModel(_db);
    }

    public void Dispose()
    {
        _vm.PluginEntries.Clear();   // 释放插件委托引用，让 ALC 能回收（否则插件程序集被钉住）
        _db.Dispose();
        // 卸载后程序集可能尚未被 GC 回收、文件锁未释放，删除失败可忽略（临时目录带 GUID，无碰撞）。
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void TodoHost_GetTasks_returns_snapshot()
    {
        _db.Tasks.Insert(new TaskItem { Title = "写周报", ListId = "list-tasks" });
        _vm.Refresh();

        var host = new TodoHost(_db, _vm, _dispatcher, "test.plugin", _ => { });

        var all = host.GetTasks(null);
        Assert.Contains(all, t => t.Title == "写周报");

        // 返回的是快照：改 VM 活对象不影响已取到的 DTO
        var snapshot = host.GetTasks("list-tasks");
        _vm.Tasks[0].Title = "改了标题";
        Assert.Equal("写周报", snapshot[0].Title);
    }

    [Fact]
    public void LoadAll_loads_export_plugin_and_registers_sidebar_entry()
    {
        var pluginDir = Path.Combine(_dir, "plugins", "com.example.export");
        Directory.CreateDirectory(pluginDir);
        foreach (var f in new[] { "ExportPlugin.dll", "ExportPlugin.deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, f), Path.Combine(pluginDir, f));
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
            "{\"id\":\"com.example.export\",\"name\":\"导出周报\",\"version\":\"1.0.0\"," +
            "\"entryAssembly\":\"ExportPlugin.dll\",\"contractVersion\":1,\"hasUi\":true}");

        var manager = new PluginManager(_db, _vm, _dispatcher);
        manager.LoadAll(Path.Combine(_dir, "plugins"));

        Assert.Contains("com.example.export", manager.LoadedPluginIds);
        Assert.Contains(_vm.PluginEntries, e => e.Label == "导出周报");

        manager.ShutdownAll();
    }
}
