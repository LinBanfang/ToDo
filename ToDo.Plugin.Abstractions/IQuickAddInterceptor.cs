namespace ToDo.Plugin.Abstractions;

/// <summary>
/// 快速添加解析拦截器：宿主在解析「添加任务」输入前先问插件链；
/// 命中则返回草稿，否则走默认逻辑。示例：自然语言「明天下午3点 交报告 #工作 !重要」。
/// </summary>
public interface IQuickAddInterceptor
{
    bool TryParse(string text, out NewTaskDraft draft);
}
