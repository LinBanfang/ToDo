namespace ToDo.Plugin.Abstractions;

/// <summary>插件私有 KV 设置，与主 settings.json 隔离；键空间按插件 Id 前缀隔离。</summary>
public interface IPluginSettings
{
    string? Get(string key);
    void Set(string key, string? value);
    void Remove(string key);
}
