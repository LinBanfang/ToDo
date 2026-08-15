namespace Spike.Contract;

/// <summary>Shared contract assembly — the host and every plugin reference the SAME
/// copy so <c>plugin is IPlugin</c> holds (single-load guarantee).</summary>
public interface IPlugin
{
    string Id { get; }
    string Name { get; }
    void Initialize();
}

/// <summary>A plugin that contributes a WPF view, used to probe how XAML/BAML
/// resolution behaves when the plugin lives outside AppDomain.BaseDirectory.</summary>
public interface IWidgetPlugin : IPlugin
{
    object CreateView();
}
