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
    private readonly TodoEvents _events;
    private readonly List<LoadedPlugin> _loaded = new();
    private string _pluginsRoot = "";

    public PluginManager(DatabaseService db, MainViewModel vm, Dispatcher dispatcher, TodoEvents events)
    {
        _db = db;
        _vm = vm;
        _dispatcher = dispatcher;
        _events = events;
        // 语言切换 → 广播给所有插件（插件据此重载自身字符串，ADR-020 §7）。
        Loc.LanguageChanged += () => _events.RaiseLanguageChanged();
    }

    public IReadOnlyList<string> LoadedPluginIds => _loaded.Select(p => p.Id).ToArray();

    public void LoadAll(string pluginsRoot)
    {
        if (!Directory.Exists(pluginsRoot))
        {
            DiagnosticLog.Info("plugin", $"插件目录不存在：{pluginsRoot}");
            return;
        }
        _pluginsRoot = pluginsRoot;
        CleanupOrphanedData(pluginsRoot);   // 删除已从磁盘移除的插件的残留数据（M4）
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

        if (!IsAppVersionCompatible(manifest.MinAppVersion))
        {
            DiagnosticLog.Warn("plugin", $"跳过 {manifest.Id}：需要应用版本 >= {manifest.MinAppVersion}（当前 {AppVersionText}）");
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

        var host = new TodoHost(_db, _vm, _dispatcher, _events, manifest.Id, RegisterSidebar);
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

    /// <summary>当前应用版本字符串（如 "1.4.0"），用于 minAppVersion 校验与日志。</summary>
    private static string AppVersionText =>
        (typeof(PluginManager).Assembly.GetName().Version ?? new Version(0, 0)).ToString(3);

    /// <summary>应用版本是否满足插件的最低版本要求。null/空白 = 无要求；无法解析 = 拒绝（畸形 manifest）。</summary>
    internal static bool IsAppVersionCompatible(string? minAppVersion)
    {
        if (string.IsNullOrWhiteSpace(minAppVersion)) return true;
        if (!Version.TryParse(minAppVersion.Trim(), out var min)) return false;
        var app = typeof(PluginManager).Assembly.GetName().Version ?? new Version(0, 0);
        return app >= min;
    }

    /// <summary>删除本地 KV 中「插件目录已不存在」的插件的残留数据（键格式 plugins/&lt;Id&gt;/…）。</summary>
    private void CleanupOrphanedData(string pluginsRoot)
    {
        var present = new HashSet<string>(
            Directory.GetDirectories(pluginsRoot).Select(d => Path.GetFileName(d)!),
            StringComparer.OrdinalIgnoreCase);

        var staleIds = _db.GetLocalKeys("plugins/")
            .Select(PluginIdFromKey)
            .Where(id => id is not null && !present.Contains(id))
            .Select(id => id!)
            .Distinct()
            .ToArray();

        foreach (var id in staleIds)
        {
            _db.RemoveLocalKeys($"plugins/{id}/");
            DiagnosticLog.Info("plugin", $"清理已移除插件 {id} 的残留数据");
        }
    }

    private static string? PluginIdFromKey(string key)
    {
        const string prefix = "plugins/";
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var rest = key.Substring(prefix.Length);
        var slash = rest.IndexOf('/');
        return slash <= 0 ? null : rest.Substring(0, slash);
    }

    /// <summary>热重载一个后台插件（hasUi=false）：Shutdown → 卸载 ALC → 从目录重新加载。
    /// UI 插件（hasUi=true）返回 false（需重启应用，ADR-020 U4）。</summary>
    public bool ReloadPlugin(string id)
    {
        var index = _loaded.FindIndex(p => p.Id == id);
        if (index < 0)
        {
            DiagnosticLog.Warn("plugin", $"重载 {id}：未加载");
            return false;
        }

        var p = _loaded[index];
        if (p.HasUi)
        {
            DiagnosticLog.Info("plugin", $"重载 {id}：UI 插件需重启应用生效");
            return false;
        }

        try { p.Plugin.Shutdown(); }
        catch (Exception ex) { DiagnosticLog.Warn("plugin", $"Shutdown {p.Id}：{ex.Message}"); }
        p.Context?.Unload();
        _loaded.RemoveAt(index);

        var dir = Path.Combine(_pluginsRoot, id);
        if (!Directory.Exists(dir))
        {
            DiagnosticLog.Warn("plugin", $"重载 {id}：目录不存在 {dir}");
            return false;
        }
        LoadOne(dir);
        return true;
    }

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
