using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using ToDo.Plugin.Abstractions;

namespace ToDo.ViewModels;

/// <summary>侧边栏插件入口的 VM 包装：把契约的 <see cref="SidebarEntry.Open"/> 包成命令。</summary>
public sealed class PluginEntryVm
{
    public string Label { get; }
    public string Icon { get; }
    public ICommand OpenCommand { get; }

    public PluginEntryVm(SidebarEntry entry, Dispatcher dispatcher)
    {
        Label = entry.Label;
        Icon = entry.Icon;
        OpenCommand = new RelayCommand(() => dispatcher.Invoke(entry.Open));
    }
}
