namespace ToDo.Plugin.Abstractions;

/// <summary>
/// 契约版本。任何破坏性变更（加/改接口成员、改 DTO 形状）都必须递增；
/// 宿主加载插件前比对 manifest 的 contractVersion，不一致则拒绝并提示升级。
/// </summary>
public static class PluginContract
{
    public const int Version = 1;
}
