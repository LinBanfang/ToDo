# To Do — 插件系统设计方案（定稿）

> 状态：设计定稿（未实施；关键加载机制已用独立 spike 验证）
> 验证代码：[spikes/plugin-loading](../spikes/plugin-loading/)（net9.0，可在分支 `feature/plugin-system` 复跑）
> 关联：[ADR-002](adr/0002-in-place-refresh-model.md) 就地更新刷新模型、[ADR-010](adr/0010-self-hosted-sync.md) 多端同步、[ADR-013](adr/0013-task-attachments.md) 附件、[ADR-014](adr/0014-list-theme.md) 列表主题、[ADR-016](adr/0016-localization-resx.md) / [ADR-017](adr/0017-runtime-language-switch.md) 本地化、[ADR-018](adr/0018-sync-lww-hlc.md) HLC 时钟、[ADR-020](adr/0020-plugin-system.md) 插件系统决策、[ROADMAP](../ROADMAP.md)

---

## 1. 概述与已验证结论

为 ToDo 引入插件系统，宿主侧提供**数据门面、事件总线、UI 扩展点、私有存储**四类扩展点；插件经**可回收 `AssemblyLoadContext` + 契约程序集单载 + `AssemblyDependencyResolver` + 反射发现**加载。

**结论先行：可行，且比原提案更乐观——.NET 9 下从外部目录 ALC 加载带编译 XAML 的 WPF 插件可用。** 唯一硬约束是：**插件一旦创建 WPF UI，就被 WPF 运行时钉住，无法热卸载**。

### 1.1 spike 实测结果（`spikes/plugin-loading`，net9.0-windows，Release/Debug 均复现）

| 实验 | 结论 | 对设计的影响 |
|---|---|---|
| U1 契约程序集单载 | `plugin is IPlugin == true` ✅ | 单载机制成立，是最核心保证 |
| U2 非 UI 插件卸载（含 `AssemblyDependencyResolver` + 实例化） | `asmAlive=false`、文件锁释放 ✅ | 后台插件可热卸载/热重载 |
| U3a 编译 XAML `UserControl` 经 ALC 从外部目录加载 | 成功 ✅（首次一次性 flake，后续 Debug/Release、隔离/合并均稳定） | 插件可用常规 WPF XAML + 代码后置 |
| U3b 纯代码 UI 经 ALC | 成功 ✅ | 兜底路径，无需 XAML 解析 |
| U3c 编译 XAML 经默认上下文 | 成功 ✅ | 标准 WPF 机制，最稳 |
| U3d `AppDomain.AssemblyResolve` 钩子 | 成功 ✅ | 若遇资源解析不稳可作确定性兜底 |
| U4 实例化 WPF 视图后卸载 | **`asmAlive=true`、文件锁不释放 ❌** | **UI 插件不可热卸载，更新 = 重启应用** |
| U6 同名多副本并存时编译 XAML | 成功 ✅ | 热重载窗口内多版本并存不破坏资源解析 |

### 1.2 由此推导出的核心模型

- **统一加载器**：所有插件走同一套 collectible ALC + `AssemblyDependencyResolver` + 契约单载，不分「UI / 非 UI」两套代码路径。
- **卸载分两类**：后台（无 UI）插件可 `Unload()` 真正卸载；UI 插件加载后常驻，「更新 = 重启应用」。插件 manifest 用 `hasUi` 声明，宿主据此决定是否允许热重载。
- **数据红线不变**：插件对数据的一切读写只经 `ITodoHost` 命令，禁止碰 `DatabaseService`/`TrackedCollection`（ADR-002/010/018）。

---

## 2. 关键决策（含优缺点）

### D1 契约暴露「纯 DTO」而非 `TaskItem`

| 选项 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A（采用）：纯 DTO** | 契约放 `TaskDto/NewTaskDraft/…`，宿主做 `TaskItem ↔ TaskDto` 映射 | 插件真的只依赖 SDK + 契约；后台线程拿到的是快照而非 UI 线程活对象；宿主模型（`ObservableObject`/`[BsonIgnore]`/`ObservableCollection`）不泄露 | 需写映射层；DTO 随模型演进需同步 |
| B：直接暴露 `TaskItem` | 契约引用 `ToDo.Core` 的 `TaskItem` | 零映射 | 插件被迫引 LiteDB + CommunityToolkit.Mvvm；`TaskItem` 是带 UI 通知语义的活对象，线程不安全；违背「插件不知 LiteDB 存在」的承诺 |

原提案 §4.1 同时写了 `GetTasks → TaskItem` 又宣称「只依赖 SDK」，自相矛盾；D1 采用 A 修正之。

### D2 契约分层：单一 `ToDo.Plugin.Abstractions`（net9.0）

| 选项 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A（采用）：单一 net9.0 契约** | DTO + `ITodoPlugin/ITodoHost/ITodoEvents/IUiHost` 全放 net9.0。`IUiHost` 签名刻意不含 WPF 类型（视图返回 `object`、资源用 `System.Uri`），故无需 net9.0-windows | 后台插件不依赖 WPF；一个程序集；无「net9.0 引用 net9.0-windows」的 TFM 冲突 | 无 |
| B：拆 `Abstractions` + `Abstractions.Wpf` | UI 扩展点单独放 net9.0-windows | 语义清晰 | 若 UI 扩展点签名不含 WPF 类型则纯属多余；且 `ITodoHost.Ui`（net9.0）引用 `IUiHost`（net9.0-windows）会制造 TFM 依赖倒挂 |

实施中确认：`IUiHost` 当前成员（`RegisterSidebarEntry`/`RegisterSettingsSection`/`MergeResourceDictionary(Uri)`）都不需要 WPF 类型，故选 A。**未来若某扩展点需在签名里直接暴露 WPF 类型（如返回 `FrameworkElement`），再拆出 net9.0-windows 契约**，届时 `ITodoHost` 通过新增的 `IUiHost` 获取方式（而非直接属性）解耦。

### D3 加载模型：统一 collectible ALC（非「UI 走默认上下文」）

| 选项 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A（采用）：统一 collectible ALC** | 所有插件一个 `PluginLoadContext` | 单一路径；后台插件热卸载（U2 验证）；UI 插件也照常工作（U3a 验证） | UI 插件实际不可卸载（U4），需在语义上区分「加载」与「卸载」 |
| B：UI 插件走默认上下文 | `AssemblyLoadContext.Default` + `Resolving` 找私有依赖 | 资源解析最稳（U3c） | 两套加载路径；默认上下文程序集永远不可卸载，UI 插件同样不可卸载，未换来收益 |

A 与 B 在「UI 插件不可卸载」上等价，A 更简单，故选 A。U3d 的 `AssemblyResolve` 钩子保留为**可选确定性兜底**（若未来在特定环境下 `;component/` 资源解析不稳，就挂上它，把 ALC 程序集喂给默认上下文的 `Assembly.Load`）。

### D4 UI 插件更新方式

| 选项 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A（采用）：更新 = 重启应用** | 下载新版本到暂存目录，应用退出时覆盖，下次启动生效 | 与主程序自更新（ADR-006「退出后脚本覆盖」）一致；绕过 U4 不可卸载约束 | UI 插件不能热更新 |
| B：热重载 UI 插件 | 卸载旧 ALC 再载新 | 体验好 | **U4 证明 WPF 会钉住程序集、文件锁不释放，做不到** |

后台（无 UI）插件不受此限，仍可热卸载重载。

### D5 插件私有数据存放

| 选项 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A（采用）：进 todo.db 独立 collection（untracked）** | 一个通用 `local_kv` collection（`DatabaseService` 暴露 `Get/SetLocalValue`），不进 `TrackedCollection`/outbox；门面按 `plugins/<Id>/settings/` 与 `plugins/<Id>/storage/` 前缀隔离 KV 与 blob | 与 ADR-013/014「单文件即数据、备份/迁移零改动」哲学一致；`ToDo.Core` 保持插件无关（通用 KV，不引入插件概念）；无孤儿文件 | 插件数据与主数据同库，坏插件可能撑大 db（设每插件大小上限 + 卸载级联清理） |
| B：插件目录下文件 | `plugins\<id>\*.json/.db` | 隔离干净，删目录即删数据 | 破坏「单文件备份」承诺；`DatabaseService.ExportTo` 备份漏掉插件数据 |

插件**代码/程序集**始终放文件目录（`%LOCALAPPDATA%\ToDo\plugins\<id>\`，需 ALC 加载与更新），只有**数据**进 DB——两者分开。

### D6 安全：先手动安装，验签落地前不做市场/自动更新

| 选项 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A（采用）：v1 手动安装** | 用户把插件目录放进 `plugins\`；宿主只加载本地 manifest，不联网下载 | 避免「同进程 full-trust + 自动下载 = 远程代码执行」，在 ROADMAP-13 验签补上之前不扩大攻击面 | 分发不便 |
| B：直接做插件市场 + 自动更新 | `index.json` + 下载 zip | 体验好 | 无验签时是严重安全回归（比主程序自更新漏洞更大） |

插件是 full-trust、与宿主同进程（`App.Database` 是 `public static`，门面是纪律不是沙箱）；真正隔离需子进程 + IPC，列为进阶项、本方案不做。

### D7 事件总线粒度

| 选项 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A（采用）：粗粒度领域事件 + 明示恢复/同步** | `TaskCreated/Changed/Completed/Canceled/Reopened/Deleted/Restored(undo)/DataSyncApplied`，命令缝处 `Raise` | 与 ADR-002「所有变更走命令」天然一致；undo（ROADMAP-5）与重复任务（ADR-015）都有显式事件，插件不猜 | 事件比「每实体变更」粗，插件若要字段级差异需自行比较 DTO |
| B：字段级变更事件 | 每个属性一个事件 | 精确 | 事件爆炸，且与 VM 命令粒度脱节，侵入大 |

重复任务「完成 → 自动生成下一实例」在事件上表现为 `TaskCompleted(旧) + TaskCreated(新)` 两条，插件按此理解。同步 `ApplySync` 走裸集合、不经过 VM 命令，故单独 `DataSyncApplied`（批量、无逐实体事件）——插件对远端到达数据只能感知「发生了同步」。

### D8 线程模型

| 选项 | 说明 | 优点 | 缺点 |
|---|---|---|---|
| **A（采用）：门面统一编组到 UI 线程** | `ITodoHost` 每个实现方法包 `Dispatcher.Invoke`；读返回 DTO 快照 | 插件可任意线程调用；不把 UI 线程活对象交给后台线程 | 每次调用跨线程开销（量级极小，可接受） |
| B：要求插件自行编组 | 文档要求插件回 Dispatcher | 宿主简单 | 极易踩坑（`InvalidOperationException`），把并发负担推给插件作者 |

D8 与 D1 配合：返回 DTO 快照而非活对象，根除「后台线程读 UI 线程正在改的 `ObservableCollection`」的数据竞争。

---

## 3. 项目结构

```
ToDo.sln（新增两个契约项目 + 样例插件）
├── ToDo.Core/                          net9.0         （不变，模型/DB/同步/本地化）
├── ToDo/                               net9.0-windows （宿主 WPF；PluginManager/门面/ALC 放这里）
│   └── Plugins/
│       ├── PluginManager.cs
│       ├── PluginLoadContext.cs
│       ├── PluginManifest.cs
│       ├── TodoHost.cs                 （ITodoHost 门面实现，桥接 App 静态单例）
│       └── TodoEvents.cs
├── ToDo.Plugin.Abstractions/           net9.0         （契约：DTO + ITodoPlugin/ITodoHost/事件/存储/IUiHost）
├── samples/ExportPlugin/               net9.0-windows （首个样例：导出 Markdown）
└── spikes/plugin-loading/              （验证代码，保留作证据）
```

契约程序集**只加载一份**：宿主项目 `ProjectReference` 两个契约项目（默认上下文）；插件 `ProjectReference` 契约时 `Private="false"`（不复制到插件目录），ALC 的 `Load` 对契约程序集名返回 `null` 强制落到默认上下文。

---

## 4. 契约定义

### 4.1 `ToDo.Plugin.Abstractions`（net9.0，零第三方依赖）

```csharp
public interface ITodoPlugin
{
    string Id { get; }        // "com.example.export"，manifest 与程序集属性一致
    string Name { get; }
    string Version { get; }
    void Initialize(ITodoHost host);
    void Shutdown();
}

public interface ITodoHost
{
    // —— 数据：命令粒度，镜像 MainViewModel 命令；全部走宿主，禁止碰 LiteDB ——
    string? ActiveListId { get; }
    IReadOnlyList<TaskListDto> GetLists();
    IReadOnlyList<TagDto>      GetTags();
    IReadOnlyList<TaskDto>     GetTasks(string? listId);   // 快照，非活对象
    TaskDto? GetTask(string id);

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
    void Notify(string title, string message);   // 系统通知（托盘/toast）
    void Log(string message);                     // 进宿主诊断日志
    string CurrentLanguage { get; }               // "zh-CN" / "en-US"
    IPluginSettings Settings { get; }             // 插件私有 KV
    IPluginStorage  Storage  { get; }             // 插件私有 blob
    IUiHost? Ui { get; }                          // WPF 宿主非空；纯后台场景可为 null
}

public interface ITodoEvents
{
    event Action<TaskDto>? TaskCreated;
    event Action<TaskDto>? TaskChanged;
    event Action<TaskDto>? TaskCompleted;
    event Action<TaskDto>? TaskCanceled;
    event Action<TaskDto>? TaskReopened;
    event Action<TaskDto>? TaskRestored;   // undo（ROADMAP-5）恢复，含删除恢复与重开恢复
    event Action<string>?  TaskDeleted;    // 参数 = taskId
    event Action?          DataSyncApplied;
    event Action?          LanguageChanged;
}

public interface IPluginSettings
{
    string? Get(string key);
    void Set(string key, string? value);
    void Remove(string key);
}

public interface IPluginStorage
{
    void Write(string key, string json);
    string? Read(string key);
    void Delete(string key);
    IEnumerable<string> Keys { get; }
}

public interface IQuickAddInterceptor
{
    bool TryParse(string text, out NewTaskDraft draft);
}
```

### 4.2 DTO（纯数据，record 快照）

```csharp
public sealed record TaskStepDto(string Id, string Title, bool Completed, int Order);

public sealed record TaskDto(
    string Id, string Title, string? Note,
    string ListId, string? GroupId, int Order,
    bool IsImportant, bool IsMyDay, int MyDayOrder,
    long? DueDate, long? Reminder, long? FiredReminder,
    string[] TagIds, TaskStepDto[] Steps,
    bool Completed, string? CloseMode, long? ClosedAt,
    long CreatedAt, long ModifiedAt);   // ⚠ ModifiedAt 是 HLC 编码（ADR-018），非墙钟，勿当时间用

public sealed record TaskListDto(string Id, string Name, string Icon, string Type, bool IsSystem, string? GroupId, int Order);
public sealed record TagDto(string Id, string Name, string Color);

public sealed class NewTaskDraft
{
    public string Title { get; set; } = "";
    public string? Note { get; set; }
    public string ListId { get; set; } = "list-tasks";
    public string? GroupId { get; set; }
    public long? DueDate { get; set; }        // Unix 毫秒
    public bool IsImportant { get; set; }
    public string[] TagIds { get; set; } = Array.Empty<string>();
}
```

**契约时间约定**：`DueDate/Reminder/CreatedAt/ClosedAt` 是 Unix 毫秒（墙钟）；`ModifiedAt` 是 HLC 编码（ADR-018），只用于比较排序，**不得**当时间显示——插件要「最近修改」用 `ClosedAt`/`CreatedAt`，或宿主另给 `CreatedAt`。

### 4.3 UI 扩展点 `IUiHost`（在 `ToDo.Plugin.Abstractions` 内）

```csharp
// 签名不含 WPF 类型（视图返回 object、资源用 System.Uri），故与契约同属 net9.0（见 D2）。
public interface IUiHost
{
    void RegisterSidebarEntry(SidebarEntry entry);
    void RegisterSettingsSection(string title, Func<object> createView);   // 返回 FrameworkElement
    void RegisterQuickAddInterceptor(IQuickAddInterceptor interceptor);
    void MergeResourceDictionary(Uri uri);   // 合并插件资源字典（主题刷用 {DynamicResource}）
}

public sealed record SidebarEntry(string Label, string Icon, int Order, Action Open);
```

---

## 5. 宿主侧实现

### 5.1 装配点（`App.OnStartup`）

```csharp
// Database → Sync → ViewModel 之后、建窗口之前
var plugins = new PluginManager(App.Database, App.ViewModel /*, Loc, ThemeService, Reminders */);
plugins.LoadAll(Path.Combine(LocalAppData, "ToDo", "plugins"));
App.Plugins = plugins;          // OnExit 时 plugins.ShutdownAll()
```

### 5.2 `PluginLoadContext`（与 spike 一致）

```csharp
sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    public PluginLoadContext(string pluginDir, string entryPath) : base(isCollectible: true)
        => _resolver = new AssemblyDependencyResolver(entryPath);

    protected override Assembly? Load(AssemblyName name)
    {
        // 契约单载：契约程序集名强制回默认上下文
        if (name.Name == "ToDo.Plugin.Abstractions")
            return null;
        var path = _resolver.ResolveAssemblyToPath(name);
        if (path != null) return LoadFromAssemblyPath(path);
        return null;   // WPF/框架程序集回默认上下文
    }
}
```

### 5.3 `PluginManager` 生命周期

```
扫描 plugins\<Id>\manifest.json
  → 校验契约版本 / minAppVersion / hasUi（版本不符则跳过并日志）
  → new PluginLoadContext(目录, entryAssembly)
  → 载入 entryAssembly → 反射找 [TodoPlugin]（或 ITodoPlugin 实现，单例）
  → Initialize(host)（host 门面内含该插件 Id 的 Settings/Storage）
ShutdownAll（退出时）：
  → 逆序 Shutdown() → 移除其 ResourceDictionary / 侧边栏入口
  → 总是 Unload() ALC：真正创建了 WPF UI 的插件被 WPF 钉住、自然无法回收（U4，文件锁保持到进程结束），
    未钉住的插件（如仅注册侧边栏入口的）会正常释放文件锁。hasUi 仅作元数据留给 M5 更新逻辑。
```

### 5.4 `TodoHost` 门面（D1/D8 的落点）

- **数据方法**：内部 `Dispatcher.Invoke` 到 UI 线程 → 转发到 `App.ViewModel` 现有命令 / `App.Database` 的 `TrackedCollection`（自动 HLC 盖章 + outbox + 派生视图刷新）。
- **读方法**：`Dispatcher.Invoke` 后从 VM 集合投影为 `TaskDto` 快照（`.ToArray()` 切断活对象引用）。
- **事件**：`TodoEvents` 在 VM 命令末尾 `Raise`（见 6）。
- **`Notify`**：转发 `ReminderService`/托盘通知；**`Log`**：转发 `DiagnosticLog`；**`CurrentLanguage`**：映射 `Loc.Language`。
- **`Settings/Storage`**：落到 `DatabaseService` 的通用 `local_kv` untracked collection（`Get/SetLocalValue`），键空间按 `plugins/<Id>/settings/` 与 `plugins/<Id>/storage/` 前缀隔离。

---

## 6. 事件总线的宿主改动

`MainViewModel` 当前无对外事件。新增 `TodoEvents`，在每个命令的**成功提交点**（`RefreshActiveTasks()` 之后）各加一行 `Raise`：

- `CreateTask` → `TaskCreated`；`CompleteTask` → `TaskCompleted`（重复任务另发 `TaskCreated(下一实例)`）；`CancelTask` → `TaskCanceled`；`Reopen` → `TaskReopened`；`DeleteTask` → `TaskDeleted`；undo 恢复 → `TaskRestored`。
- `SyncService` 拉取应用后 → `DataSyncApplied`（批量，不逐实体）。
- `Loc.LanguageChanged` → `LanguageChanged`（插件据此重载自身字符串，见 7）。

侵入面：每个命令末尾一行，可测、与 ADR-002 一致。

---

## 7. 本地化与语言切换

- 宿主 `Loc` 是强类型静态门面（221 个成员），**只服务宿主字符串**；`host.CurrentLanguage` 供插件判断当前语言（`zh-CN`/`en-US`）。`SetLanguage` 不设 `CurrentUICulture`（已核对 `LocalizationService`），所以插件**不能**依赖线程文化，必须读 `host.CurrentLanguage` + 订阅 `Events.LanguageChanged`。
- 插件 UI 字符串**自带资源**（卫星程序集或嵌入式 resx/JSON），宿主不代管。
- 语言切换会 `WindowManager.RebuildForLanguageChange()` 重建主窗口（ADR-017）。插件 UI 注册挂在 **VM 集合**上（侧边栏入口在 `MainViewModel.PluginEntries`、设置节在 `SettingsViewModel.Sections`），集合跨窗口重建存活，新窗口重建后重新绑定即恢复——无需重新注册；仅当插件用 `MergeResourceDictionary` 合并进 `Application.Resources` 时，需在卸载时移除该字典（见 §5.3）。

---

## 8. 插件数据与部署

### 8.1 目录（代码）

```
%LOCALAPPDATA%\ToDo\plugins\
  <PluginId>\
    manifest.json        # id / version / minAppVersion / contractVersion / hasUi / entryAssembly / description
    <PluginId>.dll       # 插件发布产物（framework-dependent）
    <PluginId>.deps.json # 私有 NuGet 依赖解析
    ...私有依赖 dll...
```

插件以 **framework-dependent** 发布（`dotnet publish -c Release`），主程序 self-contained（`release.yml:41`）已自带运行时，插件复用宿主运行时；`AssemblyDependencyResolver` 按插件目录内 `deps.json` 解析私有依赖。

### 8.2 数据（DB，见 D5）

`DatabaseService` 新增一个通用 untracked collection `local_kv`（`GetLocalValue`/`SetLocalValue`/`RemoveLocalValue`/`GetLocalKeys`/`RemoveLocalKeys`/`GetLocalTotalBytes`），门面按 `plugins/<Id>/settings/` 与 `plugins/<Id>/storage/` 前缀隔离；`ToDo.Core` 不引入插件概念。启动时清理「插件目录已不存在」的插件的残留数据；每插件数据总量设 10 MB 上限（门面写前检查）。备份/迁移/改库路径零改动（仍是拷一个 `todo.db`）。

### 8.3 更新

复用主程序自更新思路（下载 zip → 暂存 → 退出覆盖 → 重启）。UI 插件（`hasUi=true`）更新 = 重启生效；后台插件可卸载后原位覆盖重载。

---

## 9. 安全与边界

- **full-trust 同进程是便利与风险的权衡**：门面是纪律，不是沙箱（插件引用了 `ToDo.dll` 就能碰 `App.Database`）。v1 只面向作者自己的插件 + 手动安装；验签（ROADMAP-13）落地前不做市场/自动下载。
- 真正不可信插件 → 子进程 + IPC 沙箱（进阶项，不在本方案）。
- **依赖版本统一**：契约程序集共享（单载）；插件私有依赖走各自目录解析，宿主不与其共享版本。
- **AOT**：WPF 无 AOT，无此问题；若未来 MAUI NativeAOT，`Activator.CreateInstance` 反射需源码生成器（远期）。

---

## 10. 落地顺序（里程碑）

| 里程碑 | 内容 | 验收 |
|---|---|---|
| M1 ✅ | 契约项目（单一 net9.0）+ `PluginManager`/`PluginLoadContext`/`TodoHost` 骨架 + `samples/ExportPlugin` 跑通「加载 → Initialize → 导出到文件」 | 端到端：插件 DLL 放 `plugins\` 后侧边栏出现入口、能导出 `.md` |
| M2 ✅ | 事件总线（命令末尾 `Raise` + `ITodoEvents`）+ 门面写方法 | 单测：Create/Complete/Undo/同步各事件正确触发 + 写方法往返 |
| M3 ✅ | `IUiHost` 设置节 + 资源合并 + 快速添加拦截器 | 插件设置节进设置页、拦截器接管添加任务、字典合并 |
| M4 ✅ | manifest 校验（契约版本/minAppVersion）+ 残留数据清理 + 10 MB 大小上限 | 版本不符跳过；启动清理幽灵插件数据；超限抛异常 |
| M5 ✅ | 插件更新（`PluginUpdater`：zip SHA256 验签 → 解压 → manifest 校验 → 原子替换）+ 后台插件热重载 | 哈希不符拒绝、原子替换、UI 插件返回需重启 |
| 后续 | 插件市场 `index.json`（下载源）+ 完整代码签名（与 ROADMAP-13 合流） | 远程下载 + 内置公钥验签 |

---

## 11. 风险与边界

- **UI 插件不可热卸载（U4）**：这是 WPF 的固有行为，不是实现缺陷——设计上明确「UI 插件更新 = 重启」。
- **`ModifiedAt` 语义（ADR-018）**：契约中必须写死「`ModifiedAt` 是 HLC 编码、勿当时间」，否则插件作者会踩坑。
- **`;component/` 资源解析**：U3a 已验证可用，但 WPF 资源系统对「ALC 程序集 + pack URI」并非一等公民；若未来特定环境复现解析不稳，启用 U3d 的 `AssemblyResolve` 钩子兜底。
- **契约版本**：加接口方法即破坏性变更，靠 `contractVersion` 字段检测，宿主拒绝不符插件并提示升级。
- **本地化**：插件 UI 字符串自带资源，不假设只有 zh/en。
