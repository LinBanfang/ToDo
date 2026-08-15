using System.Windows;
using System.Windows.Controls;
using Spike.Contract;

namespace Spike.Plugin;

/// <summary>Code-only UI: builds the visual tree in C#. No compiled XAML/BAML, no
/// pack URI — so it works from a collectible ALC in an external directory.</summary>
public sealed class CodeWidgetPlugin : IWidgetPlugin
{
    public string Id => "spike.codewidget";
    public string Name => "Code-only widget";
    public void Initialize() { }

    public object CreateView()
    {
        var grid = new Grid { Margin = new Thickness(8) };
        grid.Children.Add(new TextBlock { Text = "Hello from code-only plugin view" });
        return new UserControl { Content = grid };
    }
}
