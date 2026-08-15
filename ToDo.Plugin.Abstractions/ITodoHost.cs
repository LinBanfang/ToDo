namespace ToDo.Plugin.Abstractions;

/// <summary>
/// 宿主门面：插件对数据与宿主能力的唯一入口。数据方法为命令粒度（镜像宿主 ViewModel 命令），
/// 全部经宿主转发，自动获得 HLC 盖章 / outbox / 派生视图刷新（ADR-002/018/010）。
/// 每个方法内部已编组到 UI 线程；读方法返回 DTO 快照而非活对象。
/// </summary>
public interface ITodoHost
{
    // —— 数据：读 ——
    string? ActiveListId { get; }
    IReadOnlyList<TaskListDto> GetLists();
    IReadOnlyList<TagDto> GetTags();
    /// <summary>按归属列表过滤的任务快照；<paramref name="listId"/> 为 null 时返回全部任务。</summary>
    IReadOnlyList<TaskDto> GetTasks(string? listId);
    TaskDto? GetTask(string id);

    // —— 数据：写（命令粒度；M2 事件总线落地时一并实现） ——
    TaskDto CreateTask(NewTaskDraft draft);
    void UpdateTaskTitle(string id, string title);
    void UpdateTaskNote(string id, string? note);
    void SetTaskDueDate(string id, long? dueDateUnixMs);
    void SetTaskReminder(string id, long? reminderUnixMs);
    void SetTaskImportant(string id, bool important);
    void MoveTaskToList(string id, string listId);
    void MoveTaskToGroup(string id, string? groupId);
    void AddTaskStep(string id, string title);
    void CompleteTaskStep(string id, string stepId);
    void DeleteTaskStep(string id, string stepId);
    void AddTaskTag(string id, string tagId);
    void RemoveTaskTag(string id, string tagId);
    TagDto CreateTag(string name, string color);
    void CompleteTask(string id);
    void CancelTask(string id);
    void ReopenTask(string id);
    void DeleteTask(string id);

    // —— 事件 ——
    ITodoEvents Events { get; }

    // —— 横切 ——
    void Notify(string title, string message);
    void Log(string message);
    string CurrentLanguage { get; }              // "zh-CN" / "en-US"
    IPluginSettings Settings { get; }            // 插件私有 KV（按插件 Id 隔离）
    IPluginStorage Storage { get; }              // 插件私有 blob
    IUiHost? Ui { get; }                         // WPF 宿主非空；纯后台场景可为 null

    /// <summary>弹出保存对话框并把 UTF-8 文本写入用户选择的路径；返回实际路径或 null（取消）。</summary>
    string? SaveTextFile(string suggestedName, string content, string filter = "All files (*.*)|*.*");
}
