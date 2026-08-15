using Spike.Contract;

namespace Spike.Plugin;

public sealed class SimplePlugin : IPlugin
{
    public string Id => "spike.simple";
    public string Name => "Simple (no UI)";
    public void Initialize() { }
}
