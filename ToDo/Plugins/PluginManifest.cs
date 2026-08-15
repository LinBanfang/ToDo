using System.Text.Json;

namespace ToDo.Plugins;

/// <summary>插件目录下的 manifest.json，宿主加载前先做版本兼容检查（ADR-020）。</summary>
public sealed class PluginManifest
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string EntryAssembly { get; set; } = "";
    public int? ContractVersion { get; set; }
    public string? MinAppVersion { get; set; }
    public bool HasUi { get; set; }
    public string? Description { get; set; }

    public static PluginManifest Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<PluginManifest>(json)
            ?? throw new InvalidOperationException($"manifest '{path}' 为空或非法");
    }
}
