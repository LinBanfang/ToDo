namespace ToDo.ViewModels;

/// <summary>插件注册的设置页节：<see cref="SettingsSection.Title"/> 进左侧导航，
/// <see cref="View"/> 是插件 <c>createView</c> 产出的内容（ContentControl 承载渲染）。</summary>
public sealed class PluginSettingsSection : SettingsSection
{
    public object? View { get; }

    public PluginSettingsSection(string key, string title, object? view)
    {
        Key = key;
        Title = title;
        View = view;
    }
}
