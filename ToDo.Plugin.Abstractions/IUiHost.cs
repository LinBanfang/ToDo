namespace ToDo.Plugin.Abstractions;

/// <summary>
/// 宿主 UI 扩展点（WPF 宿主非空）。接口签名刻意不含 WPF 类型（视图用 <c>object</c> 返回、
/// 资源用 <see cref="System.Uri"/>），因此放在 net9.0 契约层；若未来某个扩展点需在签名里
/// 直接暴露 WPF 类型（如返回 <c>FrameworkElement</c>），再拆出 net9.0-windows 的契约。
/// M1 只实现侧边栏入口；其余在 M3 落地。
/// </summary>
public interface IUiHost
{
    /// <summary>在侧边栏底部注册一个插件入口；点击触发 <see cref="SidebarEntry.Open"/>。</summary>
    void RegisterSidebarEntry(SidebarEntry entry);

    /// <summary>在设置页注册一节；<paramref name="createView"/> 返回 FrameworkElement。M3 实现。</summary>
    void RegisterSettingsSection(string title, Func<object> createView);

    /// <summary>注册快速添加解析拦截器。M3 实现。</summary>
    void RegisterQuickAddInterceptor(IQuickAddInterceptor interceptor);

    /// <summary>合并插件资源字典（pack:// URI）。M3 实现。</summary>
    void MergeResourceDictionary(Uri uri);
}

public sealed record SidebarEntry(string Label, string Icon, int Order, Action Open);
