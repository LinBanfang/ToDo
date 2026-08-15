# To Do — 插件系统设计方案

> 状态：提案（未实施，代码未落地）
> 关联：[ADR-002](adr/0002-in-place-refresh-model.md) 就地更新刷新模型、[ADR-010](adr/0010-self-hosted-sync.md) 多端同步、[ADR-013](adr/0013-task-attachments.md) 附件、[ADR-014](adr/0014-list-theme.md) 列表主题、[ADR-016](adr/0016-localization-resx.md) / [ADR-017](adr/0017-runtime-language-switch.md) 本地化、[ADR-018](adr/0018-sync-lww-hlc.md) HLC 时钟、[ROADMAP-13](../ROADMAP.md) 更新验签、[ROADMAP-14](../ROADMAP.md) .NET 10 升级

---

## 1. 概述

本文评估为 ToDo 引入插件系统的可行性，并给出宿主侧扩展点、插件契约、插件开发与部署方案。

**结论先行：可行。** 插件加载的核心机制（可回收 `AssemblyLoadContext` + 共享契约程序集 + `AssemblyDependencyResolver` + 反射发现）已在独立的 .NET 8 示例中验证，与框架无关，可移植到 .NET 9 / WPF。但需适配 4 处框架差异，并守住 1 条数据一致性红线。

---

## 2. 可行性评估

### 2.1 可直接复用的机制

| 机制 | 作用 | 复用结论 |
|---|---|---|
| 契约程序集只加载一份 | 保证 `plugin is ITodoPlugin` 的类型标识一致 | 原样复用；这是插件系统最核心也最易踩的坑 |
| 可回收 `AssemblyLoadContext`（`isCollectible: true`） | 隔离插件程序集 + 卸载/热重载 | 原样复用 |
| `AssemblyDependencyResolver` + `deps.json` | 插件私有依赖在插件目录内解析 | 原样复用 |
| 反射发现 + `Activator.CreateInstance` | 扫描 `ITodoPlugin` 实现并实例化 | 原样复用 |
| `Unload()` + `GC.Collect()` | 卸载后真正释放插件程序集 | 原样复用 |

### 2.2 需适配的框架差异

| 差异 | 独立示例 | ToDo | 适配动作 |
|---|---|---|---|
| TFM | `net8.0` | `net9.0-windows`（路线图计划升 net10 LTS） | 契约程序集与插件全部对齐 `net9.0-windows` |
| UI 线程 | 控制台，无 UI | WPF，单 STA 线程 | 插件回调经 `Dispatcher` 编组；插件 XAML 合并进 App 资源字典 |
| 组合根 | 极简 `ServiceRegistry` | 无 DI 容器，`App.xaml.cs` 手工装配静态单例（`App.Database / ViewModel / Reminders / Sync / Tray`） | 新增 `ITodoHost` 门面，把静态单例桥接给插件 |
| 数据一致性 | 插件无副作用 | LiteDB 单写者 + 单实例互斥 + `TrackedCollection`（outbox / HLC / 同步） | 见 2.3 红线，插件只能经宿主命令读写 |

### 2.3 数据一致性红线（必须遵守）

插件**禁止**以下行为：

1. 禁止 `new DatabaseService(...)` 自行打开 LiteDB 连接 —— 违反单实例互斥与 `Connection=direct` 单写者假设；
2. 禁止绕过 `TrackedCollection` 直接改原始集合 —— 会漏盖 `ModifiedAt`（HLC）、漏写 outbox，导致多端同步错乱；
3. 禁止绕过 `MainViewModel` 命令改任务 —— 违反 [ADR-002](adr/0002-in-place-refresh-model.md)「所有任务变更都经过 ViewModel 命令」的结论。

**插件对数据的一切读写，只能通过宿主暴露的 `ITodoHost` 命令完成**，这与 ADR-002「外部直接改 DB 需走 `Refresh()` 全量同步」的结论一致。宿主门面内部把调用转发到 `App.ViewModel` 现有命令与 `App.Database` 的 `TrackedCollection`，从而自动获得 HLC 盖章、outbox、派生视图刷新。

---

## 3. 插件示例

每个示例按三点展开：**功能 / 为什么该是插件而非通用功能 / 如何实现**。

### 3.1 导出 / 导入（iCalendar / Markdown / CSV）⭐ 建议首个落地

- **功能**：把当前列表或全部任务导出为 `.ics`（进日历）、`.md`（周报）、`.csv`（表格），并支持反向导入。
- **为什么是插件**：格式繁多且彼此独立，每种都牵一个第三方格式库（iCal.NET / CsvHelper / Markdown 生成器）；内置进 core 会让主程序被「并非人人都要」的依赖拖累。它是纯「读 + 产出文件」，副作用最小，最适合用来验证整条插件管线。
- **如何实现**：
  ```csharp
  public void Execute(IPluginContext ctx)
  {
      var tasks = host.GetTasks(host.ActiveListId);   // 只读，走宿主门面
      var ics   = IcalSerializer.Build(tasks);        // 插件自带的格式库
      host.SaveFile("tasks.ics", ics);                // 宿主提供文件对话框 / 写入
  }
  ```
  导入侧反向：解析文件 → 逐条 `host.CreateTask(new NewTaskDraft { ... })`，保证新任务进入 `TrackedCollection`、自动盖 HLC、进 outbox、触发 `RefreshActiveTasks()`。

### 3.2 番茄钟 / 专注计时

- **功能**：选中任务 → 开始 25/5 计时 → 结束给任务追加一条专注记录 → 到点用宿主通知服务提醒。
- **为什么是插件**：强个人偏好、UI 重（独立小窗 + 托盘 + 计时线程），不是待办软件核心价值；做成插件可独立快速迭代而不打扰主程序节奏。
- **如何实现**：实现后台生命周期钩子（宿主启动后 `Initialize`、退出前 `Shutdown`）；自己开计时线程，但**回 UI 必须 `Dispatcher`**。专注时长不写进 `TaskItem`（模型无此字段），存到宿主插件私有存储：
  ```csharp
  host.Storage.Write("focus-logs", new FocusLog { TaskId, Minutes, StartedAt });
  host.Notify("专注结束", "任务「写周报」完成 25 分钟");
  ```

### 3.3 GitHub issue 双向同步

- **功能**：把某个列表的任务与一个 GitHub 仓库的 issue 双向同步（标题 / 状态 / 备注 / 标签）。
- **为什么是插件**：依赖第三方 API、需 token、网络与每用户配置各不相同；core 不可能内置每一个外部服务。
- **如何实现**：后台轮询服务 + 在宿主设置页注册一节（`IUiHost.RegisterSettingsSection`）填 repo / token；token 存 `host.Settings`（插件私有 KV，不进主 `settings.json`）；任务读写仍走 `host.CreateTask / UpdateTask / CompleteTask`。

### 3.4 快速添加自然语言解析

- **功能**：在添加任务框输入「明天下午3点 交报告 #工作 !重要」，自动拆成 `DueDate` / 标签 / 重要标记 / 目标列表。
- **为什么是插件**：解析靠启发式、且**强语言相关**（中英文表达差异大），是典型「实验性、可替换」的能力；放进 core 会污染核心输入逻辑，还难做双语。
- **如何实现**：宿主在解析 `AddTaskInput` 前先问插件链：
  ```csharp
  public interface IQuickAddInterceptor
  {
      bool TryParse(string text, out NewTaskDraft draft);
  }
  ```
  插件注册进 `host.Ui.RegisterQuickAddInterceptor(this)`；宿主命中则用 `draft` 建任务，否则走默认逻辑。

### 3.5 统计仪表盘 / 习惯打卡

- **功能**：完成趋势、按时完成率、连续打卡天数等自定义视图。
- **为什么是插件**：纯读 + 展示，每个人想要的指标不同，天然适合「可选视图」而非内置死一个。
- **如何实现**：插件程序集提供 `UserControl`；宿主在侧边栏加人口（`ISidebarEntry`），点开后主内容区显示插件视图；插件的样式 / `DataTemplate` 经 `IUiHost.MergeResourceDictionary(Uri)` 合并进 App 资源，主题刷用 `{DynamicResource}` 即可跟随浅 / 深色。

---

## 4. 宿主侧设计

### 4.1 契约程序集 `ToDo.Plugin.Abstractions`

只放接口与 DTO，`net9.0-windows`，被宿主与所有插件共同引用、**只加载一份**（否则 `plugin is ITodoPlugin` 恒为 false）。

```csharp
public interface ITodoPlugin
{
    string Id { get; }
    string Name { get; }
    string Version { get; }
    void Initialize(ITodoHost host);
    void Shutdown();
}

public interface ITodoHost
{
    // —— 数据：全部走宿主命令，禁止插件碰 LiteDB ——
    IReadOnlyList<TaskItem> GetTasks(string? listId);
    TaskItem? GetTask(string id);
    void CreateTask(NewTaskDraft draft);
    void UpdateTask(TaskItem task);
    void CompleteTask(string id);
    void CancelTask(string id);

    // —— 事件总线：现有命令完成后宿主广播，插件订阅 ——
    ITodoEvents Events { get; }   // TaskCompleted / TaskChanged / ListChanged ...

    // —— 横切能力 ——
    void Notify(string title, string message);
    void Log(string message);
    string Loc(string key);            // 包一层 Loc 门面
    IPluginSettings Settings { get; }  // 插件私有 KV，隔离于主 settings.json
    IPluginStorage  Storage  { get; }  // 插件私有文件 / 集合（如例 3.2 的 focus-logs）
    IUiHost Ui { get; }                // 注册侧边栏入口 / 详情面板节 / 设置节 / 拦截器
}
```

**关键设计**：`ITodoHost` 是**精心裁剪的门面**，不要直接把巨大的 `MainViewModel` 塞给插件——那会让插件绑死内部实现。门面内部转发到 `App.ViewModel` 现有命令与 `App.Database` 的 `TrackedCollection`。

### 4.2 `PluginManager` + 可回收加载上下文

在 `App.OnStartup` 加一步（当前是 `Database` → `Sync` → `ViewModel` 的纯手工装配；插件管理器插在 `ViewModel` 之后、建窗口之前）：

```csharp
var manager = new PluginManager(App.Database, ViewModel /*, Loc, ThemeService, Reminders */);
manager.LoadAll(Path.Combine(AppContext.BaseDirectory, "plugins"));
App.Plugins = manager;   // OnExit 时 manager.UnloadAll()
```

`PluginLoadContext` 与独立示例相同，只改三处：TFM 换 `net9.0-windows`、共享程序集名换成 `ToDo.Plugin.Abstractions`、`Load` 里对「已加载于默认上下文」的程序集返回 `null`。发现 / 反射 / 卸载（`LoadAll` / `UnloadAll` / `GC.Collect`）直接复用。

### 4.3 三个 WPF 特有问题

1. **线程编组**：`ITodoHost` 实现把每个方法包一层 `Dispatcher.Invoke`（或插件侧 `Application.Current.Dispatcher`）；插件后台线程回调 UI 前必须回 Dispatcher，否则抛 `InvalidOperationException`。
2. **资源合并**：插件若要出 UI，宿主把插件的 `ResourceDictionary`（经 `IUiHost.MergeResourceDictionary(Uri)`）合并进 `Application.Current.Resources`，卸载时移除。主题刷用 `{DynamicResource}` 则插件样式跟随浅 / 深色。
3. **事件总线是新增点**：当前 `MainViewModel` 无对外事件。宿主在 `CompleteTask` / `CreateTask` 等命令末尾各加一行 `Events.Raise(...)`，侵入极小、可测，与 ADR-002「所有变更走命令」天然一致。

---

## 5. 插件开发

最简示例（只依赖 SDK，不知道 `MainViewModel` / LiteDB 的存在）：

```xml
<!-- HelloExport.csproj : net9.0-windows，Private=false 避免复制契约 DLL -->
<ProjectReference Include="..\..\ToDo.Plugin.Abstractions\ToDo.Plugin.Abstractions.csproj"
                  Private="false" />
```

```csharp
[TodoPlugin("com.example.export", "1.0.0", MinAppVersion = "2.0")]
public sealed class MarkdownExportPlugin : ITodoPlugin
{
    private ITodoHost _host = null!;

    public string Id      => "com.example.export";
    public string Name    => "Markdown 导出";
    public string Version => "1.0.0";

    public void Initialize(ITodoHost host)
    {
        _host = host;
        host.Ui.RegisterSidebarEntry("导出周报", Export);
    }

    public void Shutdown() { }

    private void Export()
    {
        var sb = new StringBuilder();
        foreach (var t in _host.GetTasks(_host.ActiveListId))
            sb.AppendLine($"- [{(t.Completed ? "x" : " ")}] {t.Title}");
        _host.SaveFile("weekly.md", sb.ToString());
    }
}
```

生命周期由 `Initialize / Shutdown` 管理；退出时宿主 `Unload` 其 ALC 并 `GC.Collect`，即完成热卸载。

---

## 6. 部署

1. **目录**：`%LOCALAPPDATA%\ToDo\plugins\<PluginId>\`，每个插件一个子目录（`deps.json` 互不冲突），与主程序目录解耦，避免自动更新覆盖主程序时误删插件。
2. **manifest**：每个插件目录放 `manifest.json`（`id / version / minAppVersion / entryAssembly / description`），宿主加载前先读 manifest 做版本兼容检查，而非盲目反射。
3. **加载顺序**：`manifest` 校验 → 契约程序集已在默认上下文 → `PluginLoadContext` 载入 `entryAssembly` → 找 `[TodoPlugin]` 类型 → `Initialize`。
4. **更新**：复用现有多源更新思路（GitHub / Gitee / 私有 appcast），或做一个「插件市场」`index.json`：下载 zip → 解压到临时目录 → 覆盖 `plugins\<Id>\` → 卸载旧 ALC 重载。
5. **签名 / 安全**：ROADMAP-13 已在考虑主程序更新验签——插件下载**同样要验签 / 校验哈希**。插件是 full-trust、与宿主同进程；真正不可信的第三方插件需走子进程 + IPC 沙箱（进阶项，不在本方案范围）。

---

## 7. 落地顺序

1. **先做契约程序集 + `PluginManager` + 一个「3.1 导出」插件**跑通全链路（门槛最低、副作用最小）；
2. **再补事件总线**（几行 `Raise`），解锁「3.2 番茄钟 / 3.3 同步」这类后台型插件；
3. **之后补 `IUiHost` 的 UI 扩展点**（侧边栏 / 详情 / 设置节），解锁「3.5 仪表盘」；
4. **最后上 manifest 校验、插件更新、验签**。

---

## 8. 风险与边界

- **依赖版本统一**：`PluginLoadContext.Load` 里「已加载则用默认上下文」的策略会强制插件与宿主统一共享依赖版本；若插件需各用各的版本，应只把契约程序序列为共享、其余一律走插件目录解析。
- **本地化**：插件 UI 字符串需自带资源（或经 `ITodoHost.Loc` 转发），不能假设只有 zh / en 两种文化。
- **AOT**：未来若走 NativeAOT，`Activator.CreateInstance` + 反射受限，需源码生成器收集插件类型。
- **安全**：full-trust 同进程是便捷与风险的权衡；不可信插件必须进程外沙箱化。
