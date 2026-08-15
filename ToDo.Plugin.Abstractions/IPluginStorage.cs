namespace ToDo.Plugin.Abstractions;

/// <summary>插件私有 blob 存储（JSON 字符串），用于专注记录等较大数据；键空间按插件 Id 前缀隔离。</summary>
public interface IPluginStorage
{
    void Write(string key, string json);
    string? Read(string key);
    void Delete(string key);
    IEnumerable<string> Keys { get; }
}
