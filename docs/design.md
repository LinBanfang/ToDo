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
| TagIds | List\<string\> | 标签 Id 列表 |
| Steps | List\<TaskStep\> | 子步骤 |
| CloseRecord | CloseRecord? | 关闭记录（null = 未关闭） |
| CreatedAt | long | 创建时间 |
| ModifiedAt | long | 修改时间 |

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
    ├── My Day / Important / 步骤 / 标签
    └── MoveTaskToList / MoveTaskToGroup
```

### 4.3 数据刷新策略

- `RefreshActiveTasks()` 是核心刷新入口，每次任务变更后调用
- 采用"就地更新"策略：`LoadLists()` 不重建集合，而是在现有对象上修改属性
- 侧边栏计数在 `RefreshActiveTasks()` 中实时计算
- 切换列表时通过 `ActiveListId`（字符串绑定）+ `OnActiveListIdChanged` 解析对象

---

## 5. UI 布局

### 5.1 主窗口（三栏布局）

```
┌──────────┬──────────────────────┬─────────────┐
│ Sidebar  │    MainContent       │ DetailPane  │
│ (280px)  │    (flex)            │ (360px)     │
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

- **ShellLayout** — CSS Grid 三栏
  - **Sidebar** — 搜索 + 系统列表 + 分割线 + 自定义列表 + 新建输入框 + 底部按钮
  - **MainContent** — 列表标题(emoji+可编辑名称) + AddTaskInput + 任务区
    - 自定义列表：GroupedTaskList（含未分组伪分组）+ 分组标题(可折叠/重命名) + 任务行
    - 系统列表：平铺 ActiveTasks
    - 已完成区：CompletedTasks
  - **TaskDetailPane** — 标题 + 截止日期菜单 + 步骤 + 标签 + 分组 + 关闭信息 + 备注 + 删除
  - **ContextMenuLayer**（通过代码动态生成）
  - **Dialogs**：TagManageDialog、DateTimeDialog

### 5.3 拖放系统

| 操作 | 拖起 | 放下 | 效果 |
|---|---|---|---|
| 同组排序 | 任务行 | 同组另一任务行 | 插入到目标位置 |
| 移到分组 | 任务行 | 分组标题 / 分组区域 | 移入该分组 |
| 移到未分组 | 任务行 | 未分组区域 | `GroupId = null` |

---

## 6. Fluent Design 实现

- 使用原生 WPF 控件 + 自定义 ControlTemplate
- 颜色：Fluent 色板（NeutralGray 系列 + AccentBlue #0078D4）
- 圆角、阴影、悬停效果
- Segoe MDL2 Assets 图标字体 + Segoe UI Emoji 彩色表情
- ComboBox、ContextMenu、MenuItem 均有自定义 Fluent 模板
- 浅色/深色主题（通过 `FluentColors.xaml` SolidColorBrush 切换）

---

## 7. 本地化

- `Services/LocalizationService.cs` — 静态 `Loc` 类，属性按 `AppLanguage` 返回中/英文
- XAML 绑定 `{x:Static services:Loc.XXX}`
- 代码中 `Loc.XXX` 引用
- 侧边栏地球按钮切换语言 → 重建窗口
- 默认中文

---

## 8. 持久化

- LiteDB 嵌入式 NoSQL，数据文件 `todo.db` 在程序目录
- 4 张表：lists / groups / tasks / tags
- 索引：ListId、GroupId、IsMyDay、IsImportant、DueDate、tagIds（多值索引）

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
