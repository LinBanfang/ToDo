using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Spike.Host;

/// <summary>Collectible ALC rooted at a plugin's deps.json. Mirrors the production
/// design: the shared contract is forced to the default context; everything else the
/// default context already has (WPF framework) resolves there too.</summary>
sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginDir, string entryPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(entryPath);
    }

    protected override Assembly? Load(AssemblyName name)
    {
        // Single-load guarantee: the contract must be the host's default-context copy,
        // otherwise "plugin is IPlugin" is false.
        if (string.Equals(name.Name, "Contract", StringComparison.OrdinalIgnoreCase))
            return null;

        var path = _resolver.ResolveAssemblyToPath(name);
        if (path != null)
            return LoadFromAssemblyPath(path);

        return null; // let the default context resolve framework assemblies
    }
}
