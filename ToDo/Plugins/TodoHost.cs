using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ToDo.Models;
using ToDo.Plugin.Abstractions;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo.Plugins;

/// <summary>
/// <see cref="ITodoHost"/> 门面实现：把静态单例（App.Database / App.ViewModel / Loc / Tray）
/// 桥接给插件。每个方法内部编组到 UI 线程；读方法返回 DTO 快照而非活对象（ADR-020 D1/D8）。
/// 写方法转发到 MainViewModel 命令，由命令缝统一盖 HLC / 写 outbox / 刷新 / Raise 事件。
/// </summary>
sealed class TodoHost : ITodoHost, IUiHost
{
    private readonly DatabaseService _db;
    private readonly MainViewModel _vm;
    private readonly Dispatcher _dispatcher;
    private readonly string _pluginId;
    private readonly Action<SidebarEntry> _registerSidebar;
    private readonly TodoEvents _events;
    private readonly PluginSettingsStore _settings;
    private readonly PluginStorageStore _storage;

    public TodoHost(DatabaseService db, MainViewModel vm, Dispatcher dispatcher, TodoEvents events,
        string pluginId, Action<SidebarEntry> registerSidebar)
    {
        _db = db;
        _vm = vm;
        _dispatcher = dispatcher;
        _events = events;
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
        _vm.Lists.Select(DtoMapper.ToList).ToArray());

    public IReadOnlyList<TagDto> GetTags() => RunOnUi(() =>
        _vm.Tags.Select(DtoMapper.ToTag).ToArray());

    public IReadOnlyList<TaskDto> GetTasks(string? listId) => RunOnUi(() =>
        _vm.Tasks
            .Where(t => listId == null || t.ListId == listId)
            .Select(DtoMapper.ToTask)
            .ToArray());

    public TaskDto? GetTask(string id) => RunOnUi(() =>
        _vm.Tasks.FirstOrDefault(t => t.Id == id) is { } t ? DtoMapper.ToTask(t) : null);

    // ─── 写（转发到 VM 命令；命令缝统一 HLC 盖章 + outbox + 刷新 + Raise 事件）──

    public TaskDto CreateTask(NewTaskDraft draft) => RunOnUi(() =>
        DtoMapper.ToTask(_vm.CreateTaskFromDraft(draft)));

    public void UpdateTaskTitle(string id, string title) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        task.Title = title;
        _vm.UpdateTaskCommand.Execute(task);
    });

    public void UpdateTaskNote(string id, string? note) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        task.Note = note;
        _vm.UpdateTaskCommand.Execute(task);
    });

    public void SetTaskDueDate(string id, long? dueDateUnixMs) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        task.DueDate = dueDateUnixMs;
        _vm.UpdateTaskCommand.Execute(task);
    });

    public void SetTaskReminder(string id, long? reminderUnixMs) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        task.Reminder = reminderUnixMs;
        _vm.UpdateTaskCommand.Execute(task);
    });

    public void SetTaskImportant(string id, bool important) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        if (task.IsImportant != important)
        {
            task.IsImportant = important;
            _vm.UpdateTaskCommand.Execute(task);
        }
    });

    public void MoveTaskToList(string id, string listId) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        var list = _vm.Lists.FirstOrDefault(l => l.Id == listId)
            ?? throw new KeyNotFoundException($"list {listId} not found");
        _vm.MoveTaskToListCommand.Execute((task, list));
    });

    public void MoveTaskToGroup(string id, string? groupId) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        var group = groupId == null ? null : _vm.Groups.FirstOrDefault(g => g.Id == groupId);
        _vm.MoveTaskToGroupCommand.Execute((task, group));
    });

    public void AddTaskStep(string id, string title) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        _vm.AddStepCommand.Execute((task, title));
    });

    public void CompleteTaskStep(string id, string stepId) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        var step = task.Steps.FirstOrDefault(s => s.Id == stepId)
            ?? throw new KeyNotFoundException($"step {stepId} not found");
        if (!step.Completed) _vm.ToggleStepCommand.Execute((task, step));
    });

    public void DeleteTaskStep(string id, string stepId) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        var step = task.Steps.FirstOrDefault(s => s.Id == stepId)
            ?? throw new KeyNotFoundException($"step {stepId} not found");
        _vm.DeleteStepCommand.Execute((task, step));
    });

    public void AddTaskTag(string id, string tagId) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        _vm.AddTagToTaskCommand.Execute((task, RequireTag(tagId)));
    });

    public void RemoveTaskTag(string id, string tagId) => RunOnUi(() =>
    {
        var task = RequireTask(id);
        _vm.RemoveTagFromTaskCommand.Execute((task, RequireTag(tagId)));
    });

    public TagDto CreateTag(string name, string color) => RunOnUi(() =>
    {
        _vm.CreateTagCommand.Execute((name, color));
        return DtoMapper.ToTag(_vm.Tags.First(t =>
            string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)));
    });

    public void CompleteTask(string id) => RunOnUi(() =>
        _vm.CloseTaskCommand.Execute((RequireTask(id), CloseMode.Complete, false)));

    public void CancelTask(string id) => RunOnUi(() =>
        _vm.CloseTaskCommand.Execute((RequireTask(id), CloseMode.Cancel, false)));

    public void ReopenTask(string id) => RunOnUi(() =>
        _vm.ReopenTaskCommand.Execute(RequireTask(id)));

    public void DeleteTask(string id) => RunOnUi(() =>
        _vm.DeleteTaskCommand.Execute(RequireTask(id)));

    private TaskItem RequireTask(string id) =>
        _vm.Tasks.FirstOrDefault(t => t.Id == id)
        ?? throw new KeyNotFoundException($"task {id} not found");

    private Tag RequireTag(string id) =>
        _vm.Tags.FirstOrDefault(t => t.Id == id)
        ?? throw new KeyNotFoundException($"tag {id} not found");

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
        RunOnUi(() =>
        {
            var view = createView();   // 插件 ALC 里创建 FrameworkElement（spike U3 已验证可用）
            _vm.Settings.Sections.Add(new PluginSettingsSection($"plugin:{_pluginId}", title, view));
        });

    public void RegisterQuickAddInterceptor(IQuickAddInterceptor interceptor) =>
        RunOnUi(() => _vm.RegisterQuickAddInterceptor(interceptor));

    public void MergeResourceDictionary(Uri uri) => RunOnUi(() =>
    {
        var app = Application.Current;
        if (app == null) return;   // headless/测试：无 App 资源可合并
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
    });

    // ─── 线程编组 ─────────────────────────────────────────────

    private T RunOnUi<T>(Func<T> f) => _dispatcher.CheckAccess() ? f() : _dispatcher.Invoke(f);
    private void RunOnUi(Action a)
    {
        if (_dispatcher.CheckAccess()) a();
        else _dispatcher.Invoke(a);
    }

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
