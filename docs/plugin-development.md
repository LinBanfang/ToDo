# To Do — 插件开发指南

> 面向插件作者：从零写一个 ToDo 插件、并发布到 `%LOCALAPPDATA%\ToDo\plugins\` 的手册。
> 契约的完整接口见 [plugin-system.md §4](plugin-system.md)；架构决策见 [ADR-020](adr/0020-plugin-system.md)。
> 可运行参考：`samples/ExportPlugin/`（导出 Markdown 周报）。

---

## 1. 插件能做什么（能力一览）

一个插件是实现了 `ITodoPlugin` 接口的类库，宿主在启动时从插件目录加载它。插件**只能**通过宿主注入的 `ITodoHost` 与宿主交互：

| 能力 | 接口 | 说明 |
|---|---|---|
| 读任务 / 列表 / 标签 | `GetTasks` / `GetLists` / `GetTags` / `GetTask` | 返回 DTO 快照 |
| 写任务 / 标签 | `CreateTask` / `UpdateTaskTitle` / `CompleteTask` / … | 命令粒度，自动同步盖章 |
| 订阅事件 | `ITodoEvents` | 任务增删改 / 完成 / 撤销 / 同步 / 语言切换 |
| 侧边栏入口 | `IUiHost.RegisterSidebarEntry` | 点击触发你的回调 |
| 设置页节 | `IUiHost.RegisterSettingsSection` | 在设置页加一节，内容是你的 WPF 视图 |
| 资源字典合并 | `IUiHost.MergeResourceDictionary` | 给宿主注入样式 / DataTemplate |
| 快速添加拦截器 | `IUiHost.RegisterQuickAddInterceptor` | 解析「添加任务」输入（自然语言等） |
| 私有设置 / 存储 | `Settings` / `Storage` | 按插件隔离的 KV / blob |
| 通知 / 日志 / 本地化 | `Notify` / `Log` / `CurrentLanguage` | 系统通知、诊断日志、当前语言 |
| 写文件 | `SaveTextFile` | 弹保存对话框写 UTF-8 文本 |

---

## 2. 快速上手

### 2.1 项目脚手架

```xml
<!-- MyPlugin.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- 默认 net9.0-windows；纯后台插件（不创建任何 WPF 视图）可用 net9.0 -->
    <TargetFramework>net9.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>   <!-- 仅当你要创建设置页节 / 自定义 WPF 视图时才需要 -->
    <RootNamespace>MyPlugin</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- Private=false：契约 DLL 不复制进插件目录，运行时由宿主单载提供 -->
    <ProjectReference Include="..\..\ToDo.Plugin.Abstractions\ToDo.Plugin.Abstractions.csproj" Private="false" />
  </ItemGroup>
</Project>
```

### 2.2 最小插件

```csharp
using ToDo.Plugin.Abstractions;

namespace MyPlugin;

public sealed class HelloPlugin : ITodoPlugin
{
    private ITodoHost _host = null!;

    public string Id      => "com.example.hello";   // 必须与 manifest id + 目录名一致（见 §5.1）
    public string Name    => "Hello";
    public string Version => "1.0.0";

    public void Initialize(ITodoHost host)
    {
        _host = host;
        host.Ui?.RegisterSidebarEntry(new SidebarEntry("Hello", "👋", 100, () =>
            host.Notify("Hello", $"当前有 {host.GetTasks(null).Count} 个任务")));
    }

    public void Shutdown() { }
}
```

### 2.3 manifest.json

放在插件目录根，字段**大小写不敏感**（推荐 camelCase）：

```json
{
  "id": "com.example.hello",      // 与插件 Id、目录名一致
  "name": "Hello",
  "version": "1.0.0",
  "entryAssembly": "MyPlugin.dll",// 入口程序集（相对插件目录）
  "contractVersion": 1,           // 必须等于 PluginContract.Version（当前 = 1）
  "hasUi": true,                  // 是否贡献 UI（侧边栏/设置节/视图）
  "minAppVersion": "1.5.0",       // 可选：最低宿主版本；不满足则跳过加载
  "description": "..."            // 可选
}
```

### 2.4 发布与部署

```powershell
dotnet publish MyPlugin\MyPlugin.csproj -c Release -o "$env:LOCALAPPDATA\ToDo\plugins\com.example.hello"
# 然后把 manifest.json 写进那个目录
```

目录结构（`Private="false"` 保证**契约 DLL 不会**被复制进来）：

```
%LOCALAPPDATA%\ToDo\plugins\com.example.hello\
  ├── manifest.json
  ├── MyPlugin.dll
  ├── MyPlugin.deps.json          # 私有 NuGet 依赖解析
  └── ...私有依赖 dll...
```

重启应用即可在侧边栏看到入口。

---

## 3. 契约速查

### 3.1 `ITodoPlugin`

```csharp
public interface ITodoPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    void Initialize(ITodoHost host);   // 宿主加载后调用一次
    void Shutdown();                   // 退出时逆序调用
}
```

### 3.2 `ITodoHost`（数据 + 横切）

读方法（返回快照，勿假定「活对象」）：

```csharp
string? ActiveListId { get; }
IReadOnlyList<TaskListDto> GetLists();
IReadOnlyList<TagDto> GetTags();
IReadOnlyList<TaskDto> GetTasks(string? listId);   // null = 全部；否则按 ListId 过滤
TaskDto? GetTask(string id);
```

写方法（命令粒度，自动盖 HLC / 写 outbox / 刷新视图，**不用自己管时间戳**）：

```csharp
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
```

横切：

```csharp
ITodoEvents Events { get; }
void Notify(string title, string message);        // 系统通知（托盘气泡）
void Log(string message);                         // 进宿主诊断日志
string CurrentLanguage { get; }                   // "zh-CN" / "en-US"
IPluginSettings Settings { get; }                 // 插件私有 KV
IPluginStorage  Storage  { get; }                 // 插件私有 blob
IUiHost? Ui { get; }                              // WPF 宿主非空；纯后台场景可为 null
string? SaveTextFile(string suggestedName, string content, string filter = "All files (*.*)|*.*");
```

### 3.3 DTO

```csharp
public sealed record TaskDto
{
    string Id; string Title; string? Note;
    string ListId; string? GroupId; int Order;
    bool IsImportant; bool IsMyDay; int MyDayOrder;
    long? DueDate;      // Unix 毫秒（墙钟）
    long? Reminder;     // Unix 毫秒（墙钟）
    long? FiredReminder;
    string[] TagIds; TaskStepDto[] Steps;
    bool Completed; string? CloseMode;  // "Complete" / "Cancel" / null
    long? ClosedAt;     // Unix 毫秒（墙钟）
    long CreatedAt;
    long ModifiedAt;    // ⚠ HLC 编码（见 §5.3），不是墙钟时间！
}
public sealed record TaskStepDto(string Id, string Title, bool Completed, int Order);
public sealed record TaskListDto { string Id, Name, Icon, Type; bool IsSystem; string? GroupId; int Order; }
// Type: "MyDay" / "Important" / "Planned" / "Tasks" / "Custom"
public sealed record TagDto(string Id, string Name, string Color);

public sealed class NewTaskDraft
{
    string Title; string? Note; string ListId = "list-tasks"; string? GroupId;
    long? DueDate; bool IsImportant; string[] TagIds;
}
```

### 3.4 `ITodoEvents`

```csharp
event Action<TaskDto>? TaskCreated;
event Action<TaskDto>? TaskChanged;
event Action<TaskDto>? TaskCompleted;
event Action<TaskDto>? TaskCanceled;
event Action<TaskDto>? TaskReopened;
event Action<TaskDto>? TaskRestored;   // 撤销删除恢复
event Action<string>?  TaskDeleted;    // 参数 = taskId
event Action?          DataSyncApplied;
event Action?          LanguageChanged;
```

### 3.5 `IUiHost`

```csharp
void RegisterSidebarEntry(SidebarEntry entry);
void RegisterSettingsSection(string title, Func<object> createView);  // createView 返回 FrameworkElement
void RegisterQuickAddInterceptor(IQuickAddInterceptor interceptor);
void MergeResourceDictionary(Uri uri);   // pack://application:,,,/MyPlugin;component/Resources.xaml

public sealed record SidebarEntry(string Label, string Icon, int Order, Action Open);
```

### 3.6 设置 / 存储 / 拦截器

```csharp
public interface IPluginSettings { string? Get(string key); void Set(string key, string? value); void Remove(string key); }
public interface IPluginStorage { void Write(string key, string json); string? Read(string key); void Delete(string key); IEnumerable<string> Keys { get; } }
public interface IQuickAddInterceptor { bool TryParse(string text, out NewTaskDraft draft); }
```

---

## 4. 进阶示例

### 4.1 订阅事件（后台型插件，无 UI）

```csharp
public void Initialize(ITodoHost host)
{
    _host = host;
    host.Events.TaskCompleted += t => host.Log($"任务「{t.Title}」完成于 {t.CloseMode}");
    host.Events.DataSyncApplied += () => host.Log("同步完成");
}
```

> 注意：事件处理器会被宿主按 `ITodoEvents` 强引用持有，插件卸载时未退订会钉住程序集（影响热重载）。若插件可能热更新，请在 `Shutdown` 里退订。

### 4.2 创建任务（含字段）

```csharp
var draft = new NewTaskDraft
{
    Title = "交报告",
    Note = "周五前",
    ListId = "list-tasks",
    DueDate = DateTimeOffset.Now.AddDays(1).ToUnixTimeMilliseconds(),
    IsImportant = true,
};
var task = _host.CreateTask(draft);   // 返回已盖章的 TaskDto
```

### 4.3 快速添加拦截器

```csharp
public void Initialize(ITodoHost host)
{
    host.Ui?.RegisterQuickAddInterceptor(new MyParser());
}

sealed class MyParser : IQuickAddInterceptor
{
    public bool TryParse(string text, out NewTaskDraft draft)
    {
        draft = null!;
        // 例如匹配「!重要」前缀；命中返回 true，否则 false 让下一个拦截器/默认逻辑处理
        if (text.StartsWith("!"))
        {
            draft = new NewTaskDraft { Title = text[1..].Trim(), IsImportant = true };
            return true;
        }
        return false;
    }
}
```

### 4.4 设置页节（需要 WPF）

```csharp
public void Initialize(ITodoHost host)
{
    host.Ui?.RegisterSettingsSection("我的插件", () => new MySettingsView());
}
```

`createView` 返回的 `FrameworkElement` 会被放进设置页的 `ContentControl` 渲染。若插件视图有独立资源，先 `MergeResourceDictionary` 注入样式。

### 4.5 私有设置 / 存储

```csharp
// 设置：小键值，自动按插件 Id 隔离，不会污染主 settings.json
_host.Settings.Set("token", "xxx");
var token = _host.Settings.Get("token");

// 存储：较大的 JSON blob（如专注记录）
_host.Storage.Write("focus-logs", json);
var logs = _host.Storage.Read("focus-logs");
```

> 每个插件数据总量上限 10 MB，写超会抛 `InvalidOperationException`。

---

## 5. 红线与坑

### 5.1 `Id` 三处必须一致

插件目录名、manifest 的 `id`、代码里的 `Id` 属性**必须相同**。宿主按 manifest `id` 做数据隔离与残留清理，目录名不一致会导致数据被当孤儿清理掉。

### 5.2 只走 `ITodoHost`，禁止碰底层

插件**不能**引用 / 实例化 `DatabaseService`、`TrackedCollection`、LiteDB——那会违反单写者 / HLC / outbox 假设（ADR-002/010/018），造成多端同步错乱。所有读写都走 `ITodoHost`。

### 5.3 `ModifiedAt` 不是时间

`TaskDto.ModifiedAt` 是 HLC 混合逻辑时钟编码（ADR-018），**只用于排序比较**，绝不能当墙钟时间显示或做时间运算。要「最近修改」用 `ClosedAt` / `CreatedAt`；要截止/提醒用 `DueDate` / `Reminder`（Unix 毫秒）。

### 5.4 `GetTasks(listId)` 是「归属列表」过滤，不是视图过滤

系统列表（我的天 / 重要 / 计划内）**不拥有任务**——任务归属要么 `list-tasks`（收件箱）、要么某自定义列表。所以 `GetTasks("list-myday")` 会返回空。要「我的天」任务，自己按 `IsMyDay`（或 `DueDate == 今天`）过滤；「重要」按 `IsImportant`；「计划内」按 `DueDate/Reminder != null`。

### 5.5 线程

`ITodoHost` 每个方法内部已编组到 UI 线程，插件可任意线程调用。但读方法返回的是**快照**，插件后台线程持有它不会看到后续变更；需要实时性就订阅事件。

### 5.6 `hasUi` 与更新

- `hasUi=false`（纯后台）：可热重载（卸载旧 ALC 再加载新版本）。
- `hasUi=true`（有 UI）：更新后**需重启应用**（WPF 会钉住旧程序集，无法热卸载）。

### 5.7 本地化

宿主的 `Loc` 是宿主字符串，插件**不能**复用。插件 UI 字符串自带资源（卫星程序集或内嵌 JSON/resx），当前语言读 `host.CurrentLanguage`，切换订阅 `Events.LanguageChanged`（宿主不设置 `CurrentUICulture`，别依赖线程文化）。

### 5.8 契约版本

加接口成员是破坏性变更，`PluginContract.Version` 会递增；宿主加载前比对 manifest 的 `contractVersion`，不符就跳过。升级插件时同步更新该字段。

---

## 6. 调试排查

- `host.Log(msg)` 写进宿主诊断日志：`<exe>\logs\app.log`（模块名 `plugin:<Id>`）。
- 插件没加载，看日志里 `[plugin]` 开头的行：
  - `跳过 …：契约版本 X != Y` → contractVersion 不符；
  - `跳过 …：需要应用版本 >= …` → minAppVersion 不满足；
  - `跳过 …：找不到 …` / `加载失败` / `初始化失败` → 按消息修。
- 侧边栏入口看不到：确认 `hasUi=true`、manifest `id` 与目录名一致、`entryAssembly` 文件名正确。

---

## 7. 参考

- 完整样例：`samples/ExportPlugin/`（侧边栏入口 + 只读门面 + SaveTextFile + Notify）
- 契约实现：`ToDo.Plugin.Abstractions/`（接口 + DTO）
- 设计/决策：[plugin-system.md](plugin-system.md) · [ADR-020](adr/0020-plugin-system.md)
