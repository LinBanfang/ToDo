using ToDo.Plugin.Abstractions;

namespace ToDo.Plugins;

/// <summary>
/// 领域事件总线实现（单一共享实例：VM 在命令缝 Raise，插件经 host.Events 订阅同一实例）。
/// 事件本身公开供插件订阅；Raise* 方法 internal，仅宿主（MainViewModel / PluginManager）可触发。
/// </summary>
public sealed class TodoEvents : ITodoEvents
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
