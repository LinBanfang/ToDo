namespace ToDo.Models;

/// <summary>
/// 通用「本地、不参与同步」的键值行（插件私有设置/存储的底层，ADR-020 D5）。
/// 与附件（ADR-013）、列表背景（ADR-014）同理：普通 collection，不进
/// <c>TrackedCollection</c> / outbox，随 todo.db 一起被备份/迁移。
/// Id 即 namespaced 键（如 <c>plugins/&lt;PluginId&gt;/&lt;key&gt;</c>）。
/// </summary>
public sealed class LocalKv
{
    public string Id { get; set; } = "";
    public string Value { get; set; } = "";
}
