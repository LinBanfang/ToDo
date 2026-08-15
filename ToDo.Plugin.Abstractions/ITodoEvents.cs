namespace ToDo.Plugin.Abstractions;

/// <summary>
/// 领域事件总线。宿主在命令成功提交后广播；undo（恢复）与重复任务（完成→生成下一实例）
/// 分别以显式事件表达，插件按此理解，不自行推断。
/// </summary>
public interface ITodoEvents
{
    event Action<TaskDto>? TaskCreated;
    event Action<TaskDto>? TaskChanged;
    event Action<TaskDto>? TaskCompleted;
    event Action<TaskDto>? TaskCanceled;
    event Action<TaskDto>? TaskReopened;
    event Action<TaskDto>? TaskRestored;   // undo 恢复（删除恢复 / 重开恢复）
    event Action<string>?  TaskDeleted;    // 参数 = taskId
    event Action?          DataSyncApplied;
    event Action?          LanguageChanged;
}
