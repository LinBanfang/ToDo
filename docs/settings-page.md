# 设置页设计方案（内嵌页面）

## 1. 目标与范围

把散落在侧边栏底部的 4 个功能按钮（标签管理 / 主题 / 语言 / 数据库路径）收敛为**一个设置齿轮入口**，并在主内容区以**内嵌页面**（方案 B）形式承载全部设置。

本方案覆盖的设置项：

| 设置项 | 现状 | 本方案 |
|---|---|---|
| 主题（浅色/深色） | footer 按钮，即时生效，已持久化 | 移入设置页，即时生效 |
| 语言（中文/英文） | footer 按钮，重建窗口，**未持久化** | 移入设置页，**持久化 + 重启生效** |
| 数据库路径 | footer 按钮打开 DbPathDialog，重启生效 | 移入设置页"数据"区，沿用 DbPathDialog |
| 更新源（GitHub/Gitee/appcast） | 仅 settings.json 可改，**无 UI** | 新增可视化编辑（增删/类型/URL） |
| 启动时检查更新 | 总是检查 | 新增开关 |
| 提醒通知 / 提示音 | 总是弹 + 响 | 新增开关 |
| 数据备份 / 恢复 | 无 | 新增导出、恢复 |

## 2. 交互与导航

```
┌──────────┬─────────────────────────────┬──────────────┐
│ Sidebar  │       MainContent           │  DetailPane  │
│          │   ┌──────────────────────┐  │ (设置模式时  │
│          │   │  列表标题 / 任务列表  │  │   隐藏)      │
│          │   └──────────────────────┘  │              │
│          │   ┌──────────────────────┐  │              │
│          │   │   设置页 (覆盖)        │  │              │
│          │   └──────────────────────┘  │              │
│ ──────── │                              │              │
│ ⚙        │                              │              │
└──────────┴─────────────────────────────┴──────────────┘
```

- **入口**：侧边栏底部唯一齿轮按钮（Segoe MDL2 `E713`，原标签管理图标位）→ `MainViewModel.IsSettingsMode = true`。
- **退出**：设置页顶部"← 返回"按钮 → `IsSettingsMode = false`。
- 进入设置模式时：主内容区切换到设置页、**详情面板隐藏**、侧边栏保留（宽度/拖动逻辑不变）。
- 设置模式下点击侧边栏任意列表 → 先退出设置模式再进入对应列表（`OpenSettings` 保存当前列表上下文）。

### 设置页布局（左导航 + 右内容，Windows 风格）

```
┌──────────┬──────────────────────────────────────┐
│ ← 返回   │  🛠 设置                              │
├──────────┼──────────────────────────────────────┤
│ 常规      │   常规  ──────────────────────────── │
│ 外观      │   语言  [中文 ▾]           ⏳ 重启生效│
│ 数据      │                                     │
│ 更新      │   外观  ──────────────────────────── │
│ 提醒      │   主题  [浅色 ▾]           ✓ 即时生效│
│           │                                     │
│           │   数据  ──────────────────────────── │
│           │   数据库路径  C:\...\todo.db  [更改] │
│           │   [导出备份]  [从备份恢复]           │
│           │                                     │
│           │   更新  ──────────────────────────── │
│           │   [x] 启动时检查更新                 │
│           │   更新源：github  api.github.com  ✕  │
│           │           gitee   gitee.com/api ✕   │
│           │   [+ 添加源]   [立即检查更新]        │
│           │                                     │
│           │   提醒  ──────────────────────────── │
│           │   [x] 启用提醒通知                   │
│           │   [x] 播放提示音                     │
└──────────┴──────────────────────────────────────┘
```

左导航用 `ListBox`（绑定节模型集合），右内容用 `ContentControl` + 每节 `DataTemplate`（按选中节切换），不引入导航框架。

## 3. 设置数据模型（settings.json）

`AppSettings`（[SettingsService.cs](../ToDo/Services/SettingsService.cs)）新增字段：

```json
{
  "SchemaVersion": 1,
  "DbPath": "%LOCALAPPDATA%\\ToDo\\todo.db",
  "Theme": "Light",
  "SidebarWidth": 280,
  "Language": "Chinese",
  "CheckForUpdatesOnStartup": true,
  "ReminderNotifications": true,
  "ReminderSound": true,
  "UpdateSources": [ { "Type": "github", "Url": "..." }, { "Type": "gitee", "Url": "..." } ]
}
```

| 字段 | 类型 | 默认 | 生效策略 |
|---|---|---|---|
| `SchemaVersion` | int | 1 | 迁移用 |
| `Language` | string | `"Chinese"` | 重启 |
| `CheckForUpdatesOnStartup` | bool | true | 下次启动 |
| `ReminderNotifications` | bool | true | 即时（15s 轮询读取） |
| `ReminderSound` | bool | true | 即时（15s 轮询读取） |

**迁移**：`SettingsService.Load()` 中若文件无 `SchemaVersion`（旧版）→ 补默认值、写 `SchemaVersion=1` 并 `Save()`，旧字段全部保留。无破坏性变更。

## 4. 各服务改动

### 4.1 SettingsService
- 新增上述字段 + `SchemaVersion` 迁移逻辑。
- 新增 `PendingRestorePath`（内部字段，用于"从备份恢复 → 重启后生效"的暂存标记，恢复后清除）。

### 4.2 本地化 Loc / 启动流程
- `App.OnStartup`：从 `SettingsService.Current.Language` 恢复 `Loc.Language`（`Loc.SetLanguage`）。
- 删除 `LanguageToggle_Click` 的重建窗口逻辑（按钮移除，入口改为设置页）。
- 语言在设置页修改 → 写 settings.json → 提示"重启后生效"（可选"立即重启"按钮）。

### 4.3 更新 UpdateService
- 把 `Configure()` 中读取源的逻辑抽成 `RefreshSources()`，`Configure()` 与 `CheckForUpdates()` 都先调用它——保证设置页改完更新源后"立即检查"用的是新源。
- 新增 `CheckForUpdatesNow()`（设置页"立即检查更新"专用）：先 `AutoUpdater.CancelRemindLater()` 清除"以后再说"的定时器与持久化日期（否则 `Start()` 会被 `Running || _remindLaterTimer != null` 守卫跳过、毫无反应），再置 `_manualCheck` 标记后检查；`OnUpdateChecked` 在手动检查时把结果反馈给用户（有新版弹 UpdateDialog、无新版弹"已是最新版本（含源最新版本号）"、失败弹首个真实网络错误——`_lastCheckError` 记录逐源 catch 的第一个异常并取最内层消息，避免显示通用的 `MissingFieldException`），后台启动检查保持静默。
- **失败细分**：`TryGetLatest` 在响应缺版本号/下载地址时抛描述性异常（`Update source '...' returned no version or download URL`，指出具体源）；`OnUpdateChecked` 失败分支优先显示 `_lastCheckError` 的最内层消息，兜底显示友好本地化文案 `UpdateSourceNoInfo`——不再出现 `MissingFieldException` 的默认文本（如 "attempted to access a non-existing field"）。
- **诊断日志**：`UpdateService` 各点位接入 `DiagnosticLog`（源列表脱敏、逐源结果、检查结论），写入 `<exe>\logs\app.log`，跨机器排查用，详见 [logging.md](logging.md)。

### 4.4 提醒 ReminderService
- `Check()` 每次轮询读取 `SettingsService.Current.ReminderNotifications / ReminderSound`：通知关 → 不 `ShowBalloonTip`；声音关 → 不 `SystemSounds.Exclamation.Play()`。即时生效，无需重启。

### 4.5 数据库 DatabaseService
- 新增 `ExportTo(string path)`：`_db.Checkpoint()` 后 `File.Copy(_dbPath, path, true)`（单用户桌面应用，可接受）。
- **恢复**流程（重启生效，契合决策 4）：
  1. 设置页选择备份文件 → 复制到 `%LOCALAPPDATA%\ToDo\pending-restore.db` → 写 `PendingRestorePath` → 提示重启。
  2. `App.OnStartup` 在 `new DatabaseService()` 之前：若 `PendingRestorePath` 存在 → 覆盖 `Current.DbPath` → 清除标记。

## 5. 新增 / 改动文件清单

| 文件 | 动作 | 说明 |
|---|---|---|
| `ViewModels/SettingsViewModel.cs` | 新增 | ObservableObject；节模型集合 + 各节属性/命令 |
| `Views/SettingsPage.xaml` (+cs) | 新增 | UserControl，左导航 + 右内容 |
| `ViewModels/MainViewModel.cs` | 改 | 加 `IsSettingsMode` + `OpenSettingsCommand`/`CloseSettingsCommand` |
| `MainWindow.xaml` | 改 | 主内容区宿主 `SettingsPage` 并切换可见性；隐藏详情面板；footer 收敛为齿轮 |
| `MainWindow.xaml.cs` | 改 | 删 `DbPath_Click`/`LanguageToggle_Click`/主题按钮处理（入口并入设置页） |
| `Services/SettingsService.cs` | 改 | 新字段 + 迁移 + `PendingRestorePath` |
| `Services/LocalizationService.cs` | 改 | 新增设置页字符串 |
| `Services/UpdateService.cs` | 改 | 抽 `RefreshSources()` |
| `Services/ReminderService.cs` | 改 | 轮询读开关 |
| `Services/DatabaseService.cs` | 改 | `ExportTo()` + 恢复辅助 |
| `App.xaml.cs` | 改 | 恢复语言、`PendingRestorePath` 处理、按开关检查更新 |
| `Views/Dialogs/DbPathDialog.xaml.cs` | 复用 | 不新增 |

## 6. 设置页本地化新增字符串

`Loc` 新增（中文/英文成对）：设置、返回、常规、外观、数据、更新、提醒、语言、主题、浅色、深色、重启生效、即时生效、数据库路径、更改、导出备份、从备份恢复、启动时检查更新、更新源、添加源、立即检查更新、类型、URL、启用提醒通知、播放提示音、恢复成功/失败提示、立即重启等。

## 7. 风险与权衡

- **代码体积**：MainWindow.xaml(.cs) 已 75KB/67KB 且高度 code-behind。方案 B 不抽取现有任务视图，仅在主内容格内**叠加**设置页并用可见性切换，改动面最小；代价是两个视图常驻同一格，属性不多、可接受。
- **LiteDB 文件复制**：`Checkpoint()` 后复制仍非绝对原子，单用户场景可接受；恢复走"暂存 + 重启替换"避免打开中的 DB 被覆盖。
- **更新源无格式校验**：URL 留空或非法由"立即检查"暴露；首版不做预校验，编辑时仅存字符串。
- **语言重启生效**：与现状（重建窗口）相比交互更重，但规避了重建窗口在设置页场景下的复杂联动（重建会连设置页一起关掉）。

## 8. 实施计划

### 阶段 1 — 设置基础设施（服务层）
1. `SettingsService`：新增 `SchemaVersion` + 迁移；新增 `Language` / `CheckForUpdatesOnStartup` / `ReminderNotifications` / `ReminderSound` / `PendingRestorePath`。
2. `App.xaml.cs`：启动恢复语言；`PendingRestorePath` 替换 DB；按开关决定是否检查更新。
3. `UpdateService`：抽 `RefreshSources()`，`Configure`/`CheckForUpdates` 复用。
4. `ReminderService`：`Check()` 读取通知/声音开关。
5. `DatabaseService`：`ExportTo()` + 恢复暂存辅助。

### 阶段 2 — 设置页框架
6. `SettingsViewModel.cs` + `SettingsPage.xaml`：左导航 + 右内容骨架（5 节占位）。
7. `MainViewModel`：`IsSettingsMode` + 开关命令。
8. `MainWindow.xaml`：主内容区宿主 + 可见性切换；详情面板隐藏；footer 收敛为齿轮按钮；删除旧入口处理。

### 阶段 3 — 各设置项接入
9. 外观：主题 ComboBox → `ThemeService.Apply` + 保存（即时）。
10. 常规：语言 ComboBox → 保存 + 重启提示。
11. 数据：当前路径展示 + "更改"（复用 DbPathDialog）+ 导出 + 恢复。
12. 更新：启动检查开关；更新源列表编辑（增删/类型/URL）；"立即检查更新"。
13. 提醒：通知/声音开关。

### 阶段 4 — 收尾
14. `Loc` 补全设置页全部字符串（中/英）。
15. 写 `docs/adr/0008-in-app-settings-page.md`，更新 `docs/design.md`（5.2 组件树 / 7 本地化 / 8 持久化 / 11 更新）与 `README.md`。
16. `dotnet build` 验证 + 手工冒烟；视情况 `chore: bump version`。
