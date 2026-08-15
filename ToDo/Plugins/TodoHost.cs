using System.IO;
using System.Text;
using System.Windows.Threading;
using ToDo.Models;
using ToDo.Plugin.Abstractions;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo.Plugins;

/// <summary>
/// <see cref="ITodoHost"/> 门面实现：把静态单例（App.Database / App.ViewModel / Loc / Tray）
/// 桥接给插件。每个方法内部编组到 UI 线程；读方法返回 DTO 快照而非活对象（ADR-020 D1/D8）。
/// 写方法（命令粒度）在 M2（事件总线）落地时实现。
/// </summary>
sealed class TodoHost : ITodoHost, IUiHost
{
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher;
    private readonly string _pluginId;
    private readonly Action<SidebarEntry> _registerSidebar;
    private readonly TodoEvents _events = new();
    private readonly PluginSettingsStore _settings;
    private readonly PluginStorageStore _storage;

    public TodoHost(DatabaseService db, MainViewModel vm, Dispatcher dispatcher,
        string pluginId, Action<SidebarEntry> registerSidebar)
    {
        _db = db;
        _vm = vm;
        _dispatcher = dispatcher;
        _pluginId = pluginId;
        _registerSidebar = registerSidebar;
        _settings = new PluginSettingsStore(db, $"plugins/{pluginId}/settings/");
        _storage = new PluginStorageStore(db, $"plugins/{pluginId}/storage/");
    }

    public ITodoEvents Events => _events;
    public IUiHost? Ui => this;
    public IPluginSettings Settings => _settings;
    public IPluginStorage Storage => _storage;

    public string CurrentLanguage => Loc.Language == AppLanguage.English ? "en-US" : "zh-CN";
    public string? ActiveListId => RunOnUi(() => _vm.ActiveListId);

    // ─── 读（快照）─────────────────────────────────────────────

    public IReadOnlyList<TaskListDto> GetLists() => RunOnUi(() =>
        _vm.Lists.Select(l => new TaskListDto
        {
            Id = l.Id,
            Name = l.Name,
            Icon = l.Icon,
            Type = l.Type.ToString(),
            IsSystem = l.IsSystem,
            GroupId = l.GroupId,
            Order = l.Order,
        }).ToArray());

    public IReadOnlyList<TagDto> GetTags() => RunOnUi(() =>
        _vm.Tags.Select(t => new TagDto(t.Id, t.Name, t.Color)).ToArray());

    public IReadOnlyList<TaskDto> GetTasks(string? listId) => RunOnUi(() =>
        _vm.Tasks
            .Where(t => listId == null || t.ListId == listId)
            .Select(ToDto)
            .ToArray());

    public TaskDto? GetTask(string id) => RunOnUi(() =>
        _vm.Tasks.FirstOrDefault(t => t.Id == id) is { } t ? ToDto(t) : null);

    // ─── 写（M2 事件总线落地时实现）───────────────────────────

    public TaskDto CreateTask(NewTaskDraft draft) => throw M2();
    public void UpdateTaskTitle(string id, string title) => throw M2();
    public void UpdateTaskNote(string id, string? note) => throw M2();
    public void SetTaskDueDate(string id, long? dueDateUnixMs) => throw M2();
    public void SetTaskReminder(string id, long? reminderUnixMs) => throw M2();
    public void SetTaskImportant(string id, bool important) => throw M2();
    public void MoveTaskToList(string id, string listId) => throw M2();
    public void MoveTaskToGroup(string id, string? groupId) => throw M2();
    public void AddTaskStep(string id, string title) => throw M2();
    public void CompleteTaskStep(string id, string stepId) => throw M2();
    public void DeleteTaskStep(string id, string stepId) => throw M2();
    public void AddTaskTag(string id, string tagId) => throw M2();
    public void RemoveTaskTag(string id, string tagId) => throw M2();
    public TagDto CreateTag(string name, string color) => throw M2();
    public void CompleteTask(string id) => throw M2();
    public void CancelTask(string id) => throw M2();
    public void ReopenTask(string id) => throw M2();
    public void DeleteTask(string id) => throw M2();

    private static NotImplementedException M2() =>
        new("该写方法在 M2（事件总线）落地时实现；当前只支持只读插件。");

    // ─── 横切 ─────────────────────────────────────────────────

    public void Notify(string title, string message) => RunOnUi(() =>
        App.Tray?.Icon.ShowBalloonTip(3000, title, message, System.Windows.Forms.ToolTipIcon.Info));

    public void Log(string message) => DiagnosticLog.Info("plugin:" + _pluginId, message);

    public string? SaveTextFile(string suggestedName, string content, string filter = "All files (*.*)|*.*") =>
        RunOnUi(() =>
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { FileName = suggestedName, Filter = filter };
            if (dlg.ShowDialog(WindowManager.CurrentWindow()) != true) return null;
            File.WriteAllText(dlg.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return dlg.FileName;
        });

    // ─── IUiHost ──────────────────────────────────────────────

    public void RegisterSidebarEntry(SidebarEntry entry) =>
        RunOnUi(() => _registerSidebar(entry));

    public void RegisterSettingsSection(string title, Func<object> createView) =>
        throw new NotImplementedException("IUiHost.RegisterSettingsSection 在 M3 实现。");

    public void RegisterQuickAddInterceptor(IQuickAddInterceptor interceptor) =>
        throw new NotImplementedException("IUiHost.RegisterQuickAddInterceptor 在 M3 实现。");

    public void MergeResourceDictionary(Uri uri) =>
        throw new NotImplementedException("IUiHost.MergeResourceDictionary 在 M3 实现。");

    // ─── 线程编组 ─────────────────────────────────────────────

    private T RunOnUi<T>(Func<T> f) => _dispatcher.CheckAccess() ? f() : _dispatcher.Invoke(f);
    private void RunOnUi(Action a)
    {
        if (_dispatcher.CheckAccess()) a();
        else _dispatcher.Invoke(a);
    }

    private static TaskDto ToDto(TaskItem t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Note = t.Note,
        ListId = t.ListId,
        GroupId = t.GroupId,
        Order = t.Order,
        IsImportant = t.IsImportant,
        IsMyDay = t.IsMyDay,
        MyDayOrder = t.MyDayOrder,
        DueDate = t.DueDate,
        Reminder = t.Reminder,
        FiredReminder = t.FiredReminder,
        TagIds = t.TagIds?.ToArray() ?? Array.Empty<string>(),
        Steps = t.Steps.Select(s => new TaskStepDto(s.Id, s.Title, s.Completed, s.Order)).ToArray(),
        Completed = t.Completed,
        CloseMode = t.CloseRecord?.CloseMode.ToString(),
        ClosedAt = t.CloseRecord?.ClosedAt,
        CreatedAt = t.CreatedAt,
        ModifiedAt = t.ModifiedAt,
    };

    // ─── 私有 KV / blob 存储（DB local_kv，ADR-020 D5）────────

    private sealed class PluginSettingsStore : IPluginSettings
    {
        private readonly DatabaseService _db;
        private readonly string _prefix;
        public PluginSettingsStore(DatabaseService db, string prefix) { _db = db; _prefix = prefix; }
        private string Key(string k) => _prefix + k;
        public string? Get(string key) => _db.GetLocalValue(Key(key));
        public void Set(string key, string? value) => _db.SetLocalValue(Key(key), value);
        public void Remove(string key) => _db.RemoveLocalValue(Key(key));
    }

    private sealed class PluginStorageStore : IPluginStorage
    {
        private readonly DatabaseService _db;
        private readonly string _prefix;
        public PluginStorageStore(DatabaseService db, string prefix) { _db = db; _prefix = prefix; }
        private string Key(string k) => _prefix + k;
        public void Write(string key, string json) => _db.SetLocalValue(Key(key), json);
        public string? Read(string key) => _db.GetLocalValue(Key(key));
        public void Delete(string key) => _db.RemoveLocalValue(Key(key));
        public IEnumerable<string> Keys =>
            _db.GetLocalKeys(_prefix).Select(k => k.Substring(_prefix.Length)).ToArray();
    }
}
