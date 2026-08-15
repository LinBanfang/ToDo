using ToDo.Plugin.Abstractions;
using ToDo.Services;

namespace ToDo.ViewModels;

public partial class MainViewModel
{
    private readonly List<IQuickAddInterceptor> _quickAddInterceptors = new();

    /// <summary>插件注册快速添加解析拦截器；宿主在 <see cref="AddTask"/> 时按注册顺序询问。</summary>
    public void RegisterQuickAddInterceptor(IQuickAddInterceptor interceptor)
        => _quickAddInterceptors.Add(interceptor);

    /// <summary>添加任务的统一入口：先问插件拦截器，命中则用草稿建任务（走 tracked Insert + 事件），
    /// 否则回退到默认标题解析。空文本忽略。</summary>
    public void AddTask(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return;

        foreach (var interceptor in _quickAddInterceptors)
        {
            try
            {
                if (interceptor.TryParse(trimmed, out var draft))
                {
                    CreateTaskFromDraft(draft);
                    return;
                }
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("plugin", $"quick-add interceptor failed: {ex.Message}");
            }
        }

        CreateTaskCommand.Execute(trimmed);
    }
}
