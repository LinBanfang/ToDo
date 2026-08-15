using ToDo.Plugin.Abstractions;

namespace ToDo.Plugins;

/// <summary>
/// 领域事件总线实现。M2（事件总线）在 MainViewModel 命令成功提交点调用内部 Raise*；
/// M1 仅提供空实现供插件订阅，暂不触发。
/// </summary>
sealed class TodoEvents : ITodoEvents
{
    public event Action<TaskDto>? TaskCreated;
    public event Action<TaskDto>? TaskChanged;
    public event Action<TaskDto>? TaskCompleted;
    public event Action<TaskDto>? TaskCanceled;
    public event Action<TaskDto>? TaskReopened;
    public event Action<TaskDto>? TaskRestored;
    public event Action<string>? TaskDeleted;
    public event Action? DataSyncApplied;
    public event Action? LanguageChanged;

    internal void RaiseTaskCreated(TaskDto t) => TaskCreated?.Invoke(t);
    internal void RaiseTaskChanged(TaskDto t) => TaskChanged?.Invoke(t);
    internal void RaiseTaskCompleted(TaskDto t) => TaskCompleted?.Invoke(t);
    internal void RaiseTaskCanceled(TaskDto t) => TaskCanceled?.Invoke(t);
    internal void RaiseTaskReopened(TaskDto t) => TaskReopened?.Invoke(t);
    internal void RaiseTaskRestored(TaskDto t) => TaskRestored?.Invoke(t);
    internal void RaiseTaskDeleted(string id) => TaskDeleted?.Invoke(id);
    internal void RaiseDataSyncApplied() => DataSyncApplied?.Invoke();
    internal void RaiseLanguageChanged() => LanguageChanged?.Invoke();
}
