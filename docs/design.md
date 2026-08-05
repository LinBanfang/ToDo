# To Do — 详细设计方案

## 1. 概述

仿 Microsoft To Do 的 WPF 桌面待办事项应用，采用 Fluent Design 风格，本地 LiteDB 存储，中英文双语。

---

## 2. 数据模型

### 2.1 TaskList（列表）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | string | 主键，系统列表固定 ID（`list-myday` 等） |
| Name | string | 列表名称 |
| Icon | string | 表情图标，空则默认 📋 |
| Type | enum | MyDay / Important / Planned / Tasks / Custom |
| IsSystem | bool | 是否系统列表 |
| GroupId | string? | 所属侧边栏列表分组 Id（null = 未分组） |
| Order | int | 排序 |
| TaskCount | int | 未关闭待办项数量（实时计算） |

### 2.2 TaskGroup（分组）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | string | 主键 |
| ListId | string | 所属列表 Id |
| Name | string | 分组名 |
| Order | int | 排序 |
| Collapsed | bool | 是否折叠 |

### 2.3 TaskItem（待办项）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | string | 主键 |
| Title | string | 标题 |
| Note | string? | 备注 |
| ListId | string | 归属列表 Id |
| GroupId | string? | 所属分组 Id（null = 未分组） |
| Order | int | 排序 |
| IsImportant | bool | 重要标记 |
| IsMyDay | bool | 我的一天标记 |
| MyDayOrder | int | 我的一天排序 |
| DueDate | long? | 截止日期（Unix 毫秒） |
| Reminder | long? | 提醒时间 |
| TagIds | List\<string\> | 标签 Id 列表（普通 List，变更需手动通知） |
| Steps | ObservableCollection\<TaskStep\> | 子步骤（实时更新，自定义 BSON 序列化） |
| Completed | bool | 完成标记（镜像 CloseRecord.CloseMode 的便捷字段） |
| CloseRecord | CloseRecord? | 关闭记录（null = 未关闭） |
| CreatedAt | long | 创建时间 |
| ModifiedAt | long | 修改时间 |

计算属性：`IsClosed`（是否关闭）、`CloseModeDisplay`（关闭方式文本）、`CompletedStepCount`（已完成步骤数）。
通知方法：`NotifyTagsChanged()` / `NotifyCloseDisplay()` / `NotifyCompletedStepCount()` —— 供就地更新模型手动触发 UI 刷新。

### 2.4 CloseRecord（关闭记录）

| 字段 | 类型 | 说明 |
|---|---|---|
| ClosedAt | long | 关闭时间戳 |
| CloseMode | enum | Complete（完成）或 Cancel（取消） |

### 2.5 Tag（标签）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | string | 主键 |
| Name | string | 标签名 |
| Color | string | 颜色（#RRGGBB） |
| CreatedAt | long | 创建时间 |

### 2.6 TaskStep（子步骤）

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | string | 主键 |
| Title | string | 步骤描述 |
| Completed | bool | 是否完成 |
| Order | int | 排序 |

### 2.7 ListGroup（侧边栏列表分组）

侧边栏自定义列表的分组容器：

| 字段 | 类型 | 说明 |
|---|---|---|
| Id | string | 主键 |
| Name | string | 分组名 |
| Order | int | 排序 |
| Collapsed | bool | 是否折叠 |

---

## 3. 系统列表逻辑

### 3.1 归属规则

一个待办项永远只属于一个列表（`ListId`）：

| 在哪新建 | ListId |
|---|---|
| Tasks / My Day / Important / Planned 视图 | `"list-tasks"`（收件箱） |
| 自定义列表 | 该列表的 Id |
| My Day 视图 | `"list-tasks"` + `IsMyDay = true` |

### 3.2 各视图过滤逻辑

| 视图 | 数据范围 | 过滤条件 |
|---|---|---|
| 📝 Tasks | 全部 | `ListId == "list-tasks"` |
| 自定义列表 | 全部 | `ListId == 该列表Id` |
| 🌅 My Day | 全部 | `IsMyDay \|\| 截止日期 == 今天` |
| ⭐ Important | 全部 | `IsImportant == true` |
| 📅 Planned | 全部 | `截止日期 != null \|\| 提醒 != null` |

### 3.3 My Day 每日重置

启动时：
- 昨天到期未完成 → `IsMyDay = false`（从 My Day 移除，不删除）
- 今天到期 → 自动加入 My Day

---

## 4. 架构

### 4.1 整体架构

```
LiteDB (todo.db)
    ↕
DatabaseService (CRUD)
    ↕
MainViewModel (状态 + 命令，CommunityToolkit.Mvvm)
    ↕
Views (XAML 数据绑定 + 事件处理)
```

### 4.2 ViewModel 核心结构

```
MainViewModel
├── 集合
│   ├── Lists / SystemLists / CustomLists
│   ├── Groups
│   ├── Tasks / ActiveTasks / CompletedTasks
│   ├── Tags
│   └── GroupedTaskList  (含未分组伪分组)
│
├── 选中状态
│   ├── ActiveList / ActiveListId
│   └── SelectedTask
│
├── 刷新方法
│   ├── LoadAll() / LoadLists() / LoadTasks()
│   └── RefreshActiveTasks()  (核心：过滤 + 分组 + 计数)
│
└── 命令（RelayCommand）
    ├── 列表 CRUD
    ├── 分组 CRUD
    ├── 任务 CRUD
    ├── 关闭系统（Complete / Cancel / Reopen / EditCloseTime）
    ├── My Day / Important / 步骤 / 标签（含 PromoteStepToTask）
    ├── MoveTaskToList / MoveTaskToGroup
    └── ToggleTheme（主题切换）
```

### 4.3 数据刷新策略

采用"就地更新 + 派生视图重建"的统一模型：

- `RefreshActiveTasks()` 是核心刷新入口，从内存 `Tasks` 集合重建所有派生视图（`ActiveTasks` / `CompletedTasks` / `GroupedTaskList`），并实时计算侧边栏计数
- **任务级变更**：直接修改内存 `Tasks` 中的实例，再调用 `RefreshActiveTasks()`，不重新读取数据库
  - 新增任务（`CreateTask` / `PromoteStepToTask`）显式 `Tasks.Add()`；删除任务 `Tasks.Remove()`，保持内存集合与数据库同步
  - 无法自通知的派生属性在命令中显式通知：`NotifyTagsChanged()`（TagIds 为普通 List）、`NotifyCloseDisplay()`（IsClosed / CloseModeDisplay）、`NotifyCompletedStepCount()`（已完成步骤数）
- **步骤级变更**：`Steps` 为 ObservableCollection，靠实时更新反映到 UI，不触发集合重建（避免步骤编辑时丢焦点）
- **全量重载**只在同步点保留：启动 `LoadAll()`、外部拖放同步 `Refresh()`、切换列表 `OnActiveListChanged`、列表 / 分组级命令
- `LoadLists()` 就地更新（在现有对象上修改属性）；重载后重新指向 `ActiveList`，并通过 `RefreshSelectedTask()` 将 `SelectedTask` 指向最新实例
- 切换列表时通过 `ActiveListId`（字符串绑定）+ `OnActiveListIdChanged` 解析对象

---

## 5. UI 布局

### 5.1 主窗口（三栏布局）

```
┌──────────┬──────────────────────┬─────────────┐
│ Sidebar  │    MainContent       │ DetailPane  │
│ (可拖动) │    (flex)            │ (360px)     │
│          │                      │ 仅选中时显示 │
├──────────┤                      │             │
│ 搜索     │  列表标题 + emoji     │  标题编辑    │
│ ──────── │  添加任务输入框       │  截止日期    │
│ 系统列表 │  ────────────────     │  步骤        │
│ ──────── │  未分组任务           │  标签        │
│ 自定义   │  ┌ 分组1       [-] ┐ │  分组        │
│ 列表     │  │  任务...        │ │  关闭信息    │
│          │  └────────────────┘ │  备注        │
│ ──────── │  ┌ 分组2       [+] ┐ │             │
│ 新建列表 │  │  (折叠)         │ │             │
│ ──────── │  └────────────────┘ │             │
│ ⚙ 🌐 🎨 │  已完成 (3)          │  删除按钮    │
└──────────┴──────────────────────┴─────────────┘
```

### 5.2 组件树

- **ShellLayout** — CSS Grid 四栏（侧边栏 + GridSplitter + 主内容 + 详情面板）；侧边栏宽度通过 `GridSplitter` 拖动调整，TwoWay 绑定 `MainViewModel.SidebarWidth`，持久化到 settings.json（180–480px 范围）
  - **Sidebar** — 搜索 + 系统列表 + 分割线 + 自定义列表 + 新建输入框 + 底部按钮
  - **MainContent** — 列表标题(emoji+可编辑名称) + AddTaskInput + 任务区
    - 自定义列表：GroupedTaskList（含未分组伪分组）+ 分组标题(可折叠/重命名) + 任务行
    - 系统列表：平铺 ActiveTasks
    - 已完成区：CompletedTasks
  - **TaskDetailPane** — 标题 + 截止日期菜单 + 步骤 + 标签 + 分组 + 关闭信息 + 备注 + 删除
  - **ContextMenuLayer**（通过代码动态生成）
  - **Dialogs**：TagManageDialog、DateTimeDialog、DbPathDialog

### 5.3 拖放系统

| 操作 | 拖起 | 放下 | 效果 |
|---|---|---|---|
| 同组排序 | 任务行 | 同组另一任务行 | 上半 → 插到目标前；下半 → 插到目标后 |
| 移到分组 | 任务行 | 分组标题 / 分组区域 | 移入该分组（折叠分组自动展开） |
| 移到未分组 | 任务行 | 未分组区域 | `GroupId = null` |
| 侧边栏列表排序 | 侧边栏列表 | 同区域另一列表（上半/下半） | 半区插入排序（系统列表固定） |
| 待办项分组排序 | 分组标题 | 另一分组标题（上半/下半） | 半区插入排序 |
| 侧边栏列表分组排序 | 侧边栏分组标题 | 另一分组标题（上半/下半） | 半区插入排序 |

所有拖起均要求鼠标位移超过 `SystemParameters.MinimumHorizontalDragDistance` / `MinimumVerticalDragDistance` 才启动拖放，避免点击时轻微手抖误触发。

拖放结束后通过 `_suppressTaskClick` 标志（而非时间窗）抑制误触发的行选中点击。

---

## 6. Fluent Design 实现

- 使用原生 WPF 控件 + 自定义 ControlTemplate
- 颜色：Fluent 色板（NeutralGray 系列 + AccentBlue #0078D4）
- 圆角、阴影、悬停效果
- Segoe MDL2 Assets 图标字体 + Segoe UI Emoji 彩色表情
- ComboBox、ContextMenu、MenuItem 均有自定义 Fluent 模板
- 浅色/深色主题：`FluentColors.xaml` 以 SolidColorBrush 定义浅色色板，深色色板由 `ThemeService` 在代码中构建（键一致）；所有主题刷引用均为 `{DynamicResource}`，侧边栏主题按钮运行时替换 `FluentColors` 字典即时生效，选择持久化到 settings.json，启动时恢复

---

## 7. 本地化

- `Services/LocalizationService.cs` — 静态 `Loc` 类，属性按 `AppLanguage` 返回中/英文
- XAML 绑定 `{x:Static services:Loc.XXX}`
- 代码中 `Loc.XXX` 引用；值转换器（相对时间 / 日期）与对话框（DbPathDialog）同样走 `Loc`
- 侧边栏地球按钮切换语言 → 重建窗口
- 默认中文

---

## 8. 持久化

- LiteDB 嵌入式 NoSQL
- 5 张集合：lists / groups / tasks / tags / listgroups
- 索引：ListId、GroupId、IsMyDay、IsImportant、DueDate、tagIds（多值索引）
- 数据库路径可配置：`SettingsService` 持久化到 `%LOCALAPPDATA%\ToDo\settings.json`，默认数据库在 `%LOCALAPPDATA%\ToDo\todo.db`；`DbPathDialog` 可更改路径并自动迁移数据；旧版程序目录下的 `todo.db` 会自动迁移到新位置
- settings.json 同时持久化主题选择（`Theme`：Light / Dark），启动时由 `App.OnStartup` 恢复

### 8.1 种子数据

首次启动时创建 4 个系统列表（带默认 emoji）：

| Id | Name | Icon | Type |
|---|---|---|---|
| list-myday | My Day / 我的一天 | ☀️ | MyDay |
| list-important | Important / 重要 | ⭐ | Important |
| list-planned | Planned / 计划内 | 📅 | Planned |
| list-tasks | Tasks / 任务 | 🏠 | Tasks |

已有数据库时自动迁移：补全系统列表缺失的 Icon 字段。

---

## 9. 待办项生命周期

```
新建 ──→ 开放 ──┬── 勾选 ──→ 完成（绿色，记录关闭时间）
                 ├── 右键取消 ──→ 取消（灰色斜线，记录关闭时间）
                 └── 右键删除 ──→ 永久删除
                       │
                 右键重新打开 ←── 完成/取消
```

关闭时间可编辑，支持设置自定义时间戳。

步骤生命周期：添加 / 行内编辑 / 拖拽排序 / 完成切换 / 升级为任务（按父任务所在列表新建任务并移除该步骤）。

删除列表：列表内任务**移入收件箱（`list-tasks`）** 而非删除，数据不丢失；列表的分组一并删除。

---

## 10. 提醒通知

- `Reminder` 记录提醒时间，参与 Planned 视图排序
- `ReminderService`（Services）每 15 秒用 LiteDB `Reminder` 索引查询到期提醒，第一次到点时通过托盘 `NotifyIcon` 弹系统通知并播放提示音
- 启动时已到期的旧提醒会被预标记，不在启动时轰炸用户
- 应用退出后不推送（需保持运行才会触发提醒）

---

## 11. 自动更新

- 应用启动空闲后查询 `https://api.github.com/repos/LinBanfang/ToDo/releases/latest`，比对最新 tag 与程序集版本（csproj `<Version>`）
- 有新版时弹出主题化 `UpdateDialog`：显示版本与发布说明，支持**立即更新**（全自动：下载 zip 到临时目录 → 写隐藏 PowerShell 更新脚本（路径 base64 编码）→ 应用退出 → 脚本等待进程结束、解压到临时目录、覆盖安装目录、重启应用）、**以后再说**（2 天后重询）、**跳过此版本**
- 先解压到临时目录再覆盖，下载损坏不会破坏当前安装；失败记录到 `%TEMP%\ToDoUpdateError.log`
- 基于 **vendored AutoUpdater.NET**（`ToDo/Updater/`，保留 `AutoUpdaterDotNET` 命名空间与 MIT 许可，`LICENSE` 随附；去掉了 WinForms UI / WebView2 / resx，宿主用自己的 WPF 对话框）
- 版本/跳过/稍后状态由 `JsonFilePersistenceProvider` 持久化到 `%LOCALAPPDATA%\ToDo\updater.json`
