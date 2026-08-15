using System.IO;
using System.Windows.Threading;
using ToDo.Plugins;
using ToDo.Services;
using ToDo.ViewModels;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// M4 测试：minAppVersion 校验、卸载残留数据清理、插件数据大小上限。
/// </summary>
[Collection("settings-shared")]
public sealed class PluginM4Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-pluginm4-" + Guid.NewGuid().ToString("N"));
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly TodoEvents _events = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public PluginM4Tests()
    {
        Directory.CreateDirectory(_dir);
        _db = new DatabaseService(Path.Combine(_dir, "todo.db"));
        SettingsService.UseDirectory(_dir);
        _vm = new MainViewModel(_db, events: _events);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // ─── minAppVersion 校验 ─────────────────────────────────

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("1.4.0", true)]
    [InlineData("1.4", true)]
    [InlineData("1.3.9", true)]
    [InlineData("99.0", false)]
    [InlineData("2.0", false)]
    [InlineData("garbage", false)]
    public void IsAppVersionCompatible_cases(string? min, bool expected)
    {
        Assert.Equal(expected, PluginManager.IsAppVersionCompatible(min));
    }

    [Fact]
    public void LoadAll_skips_plugin_with_too_high_minAppVersion()
    {
        var pluginDir = Path.Combine(_dir, "plugins", "com.example.export");
        Directory.CreateDirectory(pluginDir);
        foreach (var f in new[] { "ExportPlugin.dll", "ExportPlugin.deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, f), Path.Combine(pluginDir, f));
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
            "{\"id\":\"com.example.export\",\"name\":\"导出\",\"version\":\"1.0.0\"," +
            "\"entryAssembly\":\"ExportPlugin.dll\",\"contractVersion\":1,\"minAppVersion\":\"99.0\"}");

        var manager = new PluginManager(_db, _vm, _dispatcher, _events);
        manager.LoadAll(Path.Combine(_dir, "plugins"));

        Assert.DoesNotContain("com.example.export", manager.LoadedPluginIds);
    }

    // ─── 卸载残留数据清理 ───────────────────────────────────

    [Fact]
    public void LoadAll_cleans_orphaned_plugin_data()
    {
        _db.SetLocalValue("plugins/ghost/settings/token", "abc");
        _db.SetLocalValue("plugins/ghost/storage/log", "xyz");

        var pluginsRoot = Path.Combine(_dir, "plugins");
        Directory.CreateDirectory(pluginsRoot);

        var manager = new PluginManager(_db, _vm, _dispatcher, _events);
        manager.LoadAll(pluginsRoot);

        Assert.Null(_db.GetLocalValue("plugins/ghost/settings/token"));
        Assert.Empty(_db.GetLocalKeys("plugins/ghost/"));
    }

    // ─── 插件数据大小上限 ───────────────────────────────────

    [Fact]
    public void Storage_write_over_limit_throws()
    {
        var host = new TodoHost(_db, _vm, _dispatcher, _events, "test.plugin", _ => { });
        var big = new string('x', 10 * 1024 * 1024);   // 10 MB + key 长度 > 10 MB 上限

        Assert.Throws<InvalidOperationException>(() => host.Storage.Write("big", big));
    }
}
