using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Windows.Threading;
using ToDo.Plugins;
using ToDo.Services;
using ToDo.ViewModels;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// M5 测试：插件 zip 更新管线（SHA256 验签 → 解压 → manifest 校验 → 原子替换）+ 后台插件热重载守卫。
/// </summary>
[Collection("settings-shared")]
public sealed class PluginM5Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todo-pluginm5-" + Guid.NewGuid().ToString("N"));
    private readonly string _pluginsRoot;
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly TodoEvents _events = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public PluginM5Tests()
    {
        Directory.CreateDirectory(_dir);
        _pluginsRoot = Path.Combine(_dir, "plugins");
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

    private static (string zip, string sha) CreateZip(string workDir, string manifestJson, string? markerContent = null)
    {
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "manifest.json"), manifestJson);
        if (markerContent != null) File.WriteAllText(Path.Combine(workDir, "dummy.txt"), markerContent);
        var zip = workDir + ".zip";
        ZipFile.CreateFromDirectory(workDir, zip);
        return (zip, Sha256(zip));
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var s = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(s));
    }

    private const string Manifest = "{\"id\":\"com.example.test\",\"name\":\"测试\",\"version\":\"1.0.0\"," +
        "\"entryAssembly\":\"Test.dll\",\"contractVersion\":1,\"hasUi\":false}";

    [Fact]
    public void UpdateFromZip_installs_plugin()
    {
        var (zip, sha) = CreateZip(Path.Combine(_dir, "pkg1"), Manifest, "hello");
        var updater = new PluginUpdater(_pluginsRoot);

        var result = updater.UpdateFromZip(zip, sha);

        Assert.True(result.Success);
        Assert.Equal("com.example.test", result.PluginId);
        Assert.Equal("1.0.0", result.Version);
        Assert.False(result.RequiresRestart);
        Assert.True(File.Exists(Path.Combine(_pluginsRoot, "com.example.test", "manifest.json")));
        Assert.Equal("hello", File.ReadAllText(Path.Combine(_pluginsRoot, "com.example.test", "dummy.txt")));
    }

    [Fact]
    public void UpdateFromZip_rejects_hash_mismatch()
    {
        var (zip, _) = CreateZip(Path.Combine(_dir, "pkg2"), Manifest, "hello");
        var updater = new PluginUpdater(_pluginsRoot);

        var result = updater.UpdateFromZip(zip, "deadbeef");

        Assert.False(result.Success);
        Assert.Contains("SHA256", result.Error);
        Assert.False(Directory.Exists(Path.Combine(_pluginsRoot, "com.example.test")));
    }

    [Fact]
    public void UpdateFromZip_rejects_missing_manifest()
    {
        var workDir = Path.Combine(_dir, "pkg3");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "dummy.txt"), "no manifest");
        var zip = workDir + ".zip";
        ZipFile.CreateFromDirectory(workDir, zip);
        var updater = new PluginUpdater(_pluginsRoot);

        var result = updater.UpdateFromZip(zip, Sha256(zip));

        Assert.False(result.Success);
        Assert.Contains("manifest.json", result.Error);
    }

    [Fact]
    public void UpdateFromZip_rejects_bad_contract_version()
    {
        var bad = Manifest.Replace("\"contractVersion\":1", "\"contractVersion\":999");
        var (zip, sha) = CreateZip(Path.Combine(_dir, "pkg4"), bad, "x");
        var updater = new PluginUpdater(_pluginsRoot);

        var result = updater.UpdateFromZip(zip, sha);

        Assert.False(result.Success);
        Assert.Contains("契约版本", result.Error);
    }

    [Fact]
    public void UpdateFromZip_rejects_too_high_minAppVersion()
    {
        var bad = Manifest.Replace("\"hasUi\":false", "\"hasUi\":false,\"minAppVersion\":\"99.0\"");
        var (zip, sha) = CreateZip(Path.Combine(_dir, "pkg5"), bad, "x");
        var updater = new PluginUpdater(_pluginsRoot);

        var result = updater.UpdateFromZip(zip, sha);

        Assert.False(result.Success);
        Assert.Contains("应用版本", result.Error);
    }

    [Fact]
    public void UpdateFromZip_replaces_existing_plugin()
    {
        var updater = new PluginUpdater(_pluginsRoot);
        var (zip1, sha1) = CreateZip(Path.Combine(_dir, "pkg6a"), Manifest, "v1");
        Assert.True(updater.UpdateFromZip(zip1, sha1).Success);

        var manifest2 = Manifest.Replace("\"version\":\"1.0.0\"", "\"version\":\"2.0.0\"");
        var (zip2, sha2) = CreateZip(Path.Combine(_dir, "pkg6b"), manifest2, "v2");
        var result = updater.UpdateFromZip(zip2, sha2);

        Assert.True(result.Success);
        Assert.Equal("2.0.0", result.Version);
        Assert.Equal("v2", File.ReadAllText(Path.Combine(_pluginsRoot, "com.example.test", "dummy.txt")));
        // 无残留备份目录
        Assert.DoesNotContain(Directory.GetDirectories(_pluginsRoot), d => d.Contains(".old-"));
    }

    [Fact]
    public void ReloadPlugin_returns_false_for_ui_plugin()
    {
        var pluginDir = Path.Combine(_pluginsRoot, "com.example.export");
        Directory.CreateDirectory(pluginDir);
        foreach (var f in new[] { "ExportPlugin.dll", "ExportPlugin.deps.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, f), Path.Combine(pluginDir, f));
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"),
            "{\"id\":\"com.example.export\",\"name\":\"导出\",\"version\":\"1.0.0\"," +
            "\"entryAssembly\":\"ExportPlugin.dll\",\"contractVersion\":1,\"hasUi\":true}");

        var manager = new PluginManager(_db, _vm, _dispatcher, _events);
        manager.LoadAll(_pluginsRoot);
        Assert.Contains("com.example.export", manager.LoadedPluginIds);

        Assert.False(manager.ReloadPlugin("com.example.export"));   // UI 插件需重启
        Assert.Contains("com.example.export", manager.LoadedPluginIds);
    }
}
