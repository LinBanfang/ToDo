# ADR-020: 插件系统（加载机制 + 门面 + UI 卸载约束）

## 状态

已采纳（设计定稿，未实施）

## 背景

需要为 ToDo 引入完整插件系统：导出/导入、番茄钟、GitHub 同步、自然语言解析、统计仪表盘等能力按需装载，不拖累主程序依赖，也不污染核心输入/数据逻辑。关键约束来自既有架构：

- **数据一致性**（ADR-002/010/018）：一切写入经 `TrackedCollection`（HLC 盖章 + outbox），一切变更经 `MainViewModel` 命令；`ApplySync` 走裸集合绕过 outbox。
- **单文件即数据**（ADR-001/013/014）：本地不参与同步的数据放 DB 独立 collection，备份/迁移/改库路径 = 拷一个 `todo.db`。
- **语言切换重建窗口**（ADR-017）：`Loc.LanguageChanged` → `WindowManager.RebuildForLanguageChange()`。
- **发布方式**：self-contained win-x64 文件夹发布（`release.yml`），有 `ToDo.deps.json`，`AssemblyDependencyResolver` 可用。

## 决策

1. **加载机制**：可回收 `AssemblyLoadContext`（`isCollectible:true`）+ 契约程序集单载（ALC `Load` 对契约名返回 null）+ `AssemblyDependencyResolver` + 反射发现。已用独立 spike（`spikes/plugin-loading`）验证：契约单载成立、非 UI 插件可干净卸载、编译 XAML `UserControl` 可从 `%LOCALAPPDATA%` 外部目录经 ALC 加载。
2. **契约分层**：`ToDo.Plugin.Abstractions`（net9.0，纯 DTO + 接口）+ `ToDo.Plugin.Abstractions.Wpf`（net9.0-windows，UI 扩展点）。契约只暴露**纯 DTO 快照**，不暴露 `TaskItem`（避免插件依赖 LiteDB/CommunityToolkit.Mvvm，避免 UI 线程活对象跨线程）。
3. **门面**：`ITodoHost` 是命令粒度门面（镜像 VM 命令），内部 `Dispatcher.Invoke` 编组到 UI 线程，转发到 `App.ViewModel`/`App.Database`；读返回 DTO 快照。
4. **事件总线**：`ITodoEvents` 粗粒度领域事件（`TaskCreated/Changed/Completed/Canceled/Reopened/Deleted/Restored/DataSyncApplied/LanguageChanged`），在 VM 命令成功提交点 `Raise`；undo 与重复任务分别以显式事件表达。
5. **UI 插件不可热卸载**（spike U4）：插件一旦创建 WPF UI 就被 WPF 钉住（`asmAlive=true`、文件锁不释放）。故：后台插件可卸载热重载；UI 插件「更新 = 重启应用」。
6. **插件私有数据进 DB**：`plugin_settings`/`plugin_storage` 两个 untracked collection，键按插件 Id 前缀隔离；插件代码仍在文件目录。
7. **安全**：v1 手动安装、不联网下载；验签（ROADMAP-13）落地前不做插件市场/自动更新。

## 后果

- **优点**：数据红线守住（插件只能走门面）；后台插件可热重载；UI 插件可用常规 WPF XAML；备份/迁移零改动；依赖隔离。
- **权衡**：UI 插件更新需重启；`TaskItem ↔ TaskDto` 映射层有维护成本；full-trust 同进程是纪律而非沙箱。
- **限制**：`ModifiedAt` 是 HLC 编码（ADR-018），契约必须写明「勿当时间」；`SetLanguage` 不设 `CurrentUICulture`，插件须读 `host.CurrentLanguage` 而非线程文化。
