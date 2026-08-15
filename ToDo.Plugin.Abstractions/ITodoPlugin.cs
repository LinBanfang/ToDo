namespace ToDo.Plugin.Abstractions;

/// <summary>
/// 插件入口。宿主反射发现实现类并调用 <see cref="Initialize"/>；
/// 生命周期由宿主管理，退出时逆序调用 <see cref="Shutdown"/>。
/// </summary>
public interface ITodoPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    void Initialize(ITodoHost host);
    void Shutdown();
}
