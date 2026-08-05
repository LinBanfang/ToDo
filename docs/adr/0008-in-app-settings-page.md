# ADR-008: 内嵌设置页（主内容区页面切换）

## 状态
已采纳

## 背景
设置散落在侧边栏底部 4 个图标按钮（标签管理 / 主题 / 语言 / 数据库路径），语言未持久化、更新源无编辑 UI，且入口不统一。需要一个集中设置界面，覆盖主题、语言、数据库路径、更新源、启动检查更新、提醒开关、数据备份/恢复。

## 决策
- **载体：内嵌页面**（方案 B），主内容区在任务视图与设置页之间切换，而非模态对话框。`MainViewModel.IsSettingsMode` 控制；主内容格内**叠加**设置页（`SettingsPage` UserControl），用 `BoolToVis`/`InverseBoolToVis` 切换可见性，不抽取现有 75KB 任务视图，详情面板在进入设置时通过清空 `SelectedTask` 隐藏。
- **入口收敛**：footer 只留一个设置齿轮（`E713`），主题/语言/数据库路径移入设置页；标签管理作为设置页内的按钮打开原 `TagManageDialog`。
- **设置页结构**：左导航 + 右内容，5 节（常规/外观/数据/更新/提醒）。节为具体类（`GeneralSection` 等），右 `ContentControl` 按运行时类型选择 `DataTemplate`，不引入导航框架。
- **生效策略**：主题、提醒开关即时（提醒每 15s 轮询读设置）；更新源改完点"立即检查"生效（`UpdateService.RefreshSources()`）；语言、数据库恢复**重启生效**。
- **持久化**：settings.json 加 `SchemaVersion`（当前 1），新增 `Language` / `CheckForUpdatesOnStartup` / `ReminderNotifications` / `ReminderSound` / `PendingRestorePath`。旧文件在 `Load()` 时补默认值并盖章，无破坏性迁移。
- **数据备份/恢复**：导出 = `LiteDatabase.Checkpoint()` 后复制文件；恢复 = 复制到 `pending-restore.db` 暂存 → 设置 `PendingRestorePath` → 下次启动 `App.OnStartup` 在打开数据库前替换。

## 后果
- 优点：入口统一、语言持久化补齐、更新源可 UI 编辑、数据可备份恢复。
- 权衡：任务视图与设置页常驻同一格（可见性切换，非卸载）；语言/恢复重启生效（交互更重，但规避了窗口重建在设置页场景下的复杂联动）；LiteDB 文件复制非绝对原子（单用户场景可接受）；`MainViewModel.ToggleThemeCommand` 变为无绑定（保留，设置页直接走 `ThemeService`）。
