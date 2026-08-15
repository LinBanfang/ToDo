using System.Reflection;
using System.Runtime.Loader;

namespace ToDo.Plugins;

/// <summary>
/// 可回收 ALC，根在插件的 deps.json。契约程序集名强制回默认上下文（单载保证），
/// 其余依赖优先从插件目录解析，未命中回默认上下文（WPF/框架程序集）。
/// 与 spikes/plugin-loading 验证一致（ADR-020）。
/// </summary>
sealed class PluginLoadContext : AssemblyLoadContext
{
    private static readonly string[] Shared = { "ToDo.Plugin.Abstractions" };

    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginDir, string entryPath) : base(isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(entryPath);

    protected override Assembly? Load(AssemblyName name)
    {
        if (Array.Exists(Shared, s => string.Equals(s, name.Name, StringComparison.OrdinalIgnoreCase)))
            return null;   // 契约单载：落在宿主的默认上下文实例

        var path = _resolver.ResolveAssemblyToPath(name);
        if (path != null)
            return LoadFromAssemblyPath(path);

        return null;       // WPF / 框架程序集回默认上下文
    }
}
