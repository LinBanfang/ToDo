using Spike.Contract;

namespace Spike.Plugin;

/// <summary>Compiled-XAML plugin: CreateView news up a UserControl whose ctor calls
/// InitializeComponent → Application.LoadComponent(pack://...;component/widgetview.xaml).</summary>
public sealed class WidgetPlugin : IWidgetPlugin
{
    public string Id => "spike.widget";
    public string Name => "Widget (compiled XAML)";
    public void Initialize() { }
    public object CreateView() => new WidgetView();
}
