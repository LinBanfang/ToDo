using System.IO;
using System.Reflection;
using System.Windows.Threading;
using ToDo.Plugin.Abstractions;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo.Plugins;

/// <summary>
/// 扫描 plugins\&lt;Id&gt;\manifest.json → 校验契约版本 → 载入 entryAssembly →
/// 反射发现 <see cref="ITodoPlugin"/> → Initialize(host)。退出时逆序 Shutdown，
/// 后台插件 Unload，UI 插件只 Shutdown 不 Unload（WPF 会钉住，ADR-020 U4）。
/// </summary>
public sealed class PluginManager
{
    private sealed record LoadedPlugin(string Id, bool HasUi, ITodoPlugin Plugin, PluginLoadContext? Context);

    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher;
    private readonly List<LoadedPlugin> _loaded = new();

    public PluginManager(DatabaseService db, MainViewModel vm, Dispatcher dispatcher)
    {
        _db = db;
        _vm = vm;
        _dispatcher = dispatcher;
    }

    public IReadOnlyList<string> LoadedPluginIds => _loaded.Select(p => p.Id).ToArray();

    public void LoadAll(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            DiagnosticLog.Info("plugin", $"插件目录不存在：{pluginsRoot}");
            return;
        }
        foreach (var dir in Directory.GetDirectories(pluginsRoot))
            LoadOne(dir);
    }

    private void LoadOne(string dir)
    {
        var manifestPath = Path.Combine(dir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            DiagnosticLog.Warn("plugin", $"跳过 {dir}：缺 manifest.json");
            return;
        }

        PluginManifest manifest;
        try { manifest = PluginManifest.Load(manifestPath); }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("plugin", $"manifest 解析失败 {dir}：{ex.Message}");
            return;
        }

        if (manifest.ContractVersion is { } cv && cv != PluginContract.Version)
        {
            DiagnosticLog.Warn("plugin", $"跳过 {manifest.Id}：契约版本 {cv} != {PluginContract.Version}");
            return;
        }

        var entryPath = Path.Combine(dir, manifest.EntryAssembly);
        if (!File.Exists(entryPath))
        {
            DiagnosticLog.Warn("plugin", $"跳过 {manifest.Id}：找不到 {manifest.EntryAssembly}");
            return;
        }

        var alc = new PluginLoadContext(dir, entryPath);
        Assembly asm;
        try { asm = alc.LoadFromAssemblyPath(entryPath); }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("plugin", $"加载失败 {manifest.Id}：{ex.Message}");
            return;
        }

        var type = asm.GetTypes().FirstOrDefault(t =>
            typeof(ITodoPlugin).IsAssignableFrom(t) && !t.IsAbstract);
        if (type == null)
        {
            DiagnosticLog.Warn("plugin", $"跳过 {manifest.Id}：未找到 ITodoPlugin 实现");
            return;
        }

        ITodoPlugin plugin;
        try { plugin = (ITodoPlugin)Activator.CreateInstance(type)!; }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("plugin", $"实例化失败 {manifest.Id}：{ex.Message}");
            return;
        }

        var host = new TodoHost(_db, _vm, _dispatcher, manifest.Id, RegisterSidebar);
        try { plugin.Initialize(host); }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("plugin", $"初始化失败 {manifest.Id}：{ex.Message}");
            return;
        }

        _loaded.Add(new LoadedPlugin(manifest.Id, manifest.HasUi, plugin, alc));
        DiagnosticLog.Info("plugin", $"已加载 {manifest.Id} v{manifest.Version}" + (manifest.HasUi ? " (UI)" : ""));
    }

    private void RegisterSidebar(SidebarEntry entry) =>
        _vm.PluginEntries.Add(new PluginEntryVm(entry, _dispatcher));

    public void ShutdownAll()
    {
        foreach (var p in _loaded.AsEnumerable().Reverse())
        {
            try { p.Plugin.Shutdown(); }
            catch (Exception ex) { DiagnosticLog.Warn("plugin", $"Shutdown {p.Id}：{ex.Message}"); }

            // 总是尝试卸载 ALC：真正创建了 WPF UI 的插件会被 WPF 钉住、自然无法回收（ADR-020 U4，
            // 其文件锁保持到进程结束），而未钉住的插件（如仅注册侧边栏入口的）会正常释放文件锁。
            // hasUi 仅作为元数据留给 M5 更新逻辑决定「热重载 vs 重启」，不再 gate 这里的卸载。
            p.Context?.Unload();
        }
        _loaded.Clear();
    }
}
