# ADR-014: 任务列表主题（背景颜色 / 图片）

## 状态
已采纳（v1：纯色同步，图片字节本地存储不同步；v2：背景强弱按列表单独设置、本地存储；v3：卡片不透明度按列表单独设置、本地存储）

## 背景
用户希望每个任务列表（系统列表与自定义列表）能设置背景颜色或背景图片，给不同列表不同的视觉氛围。任务条目本身已是圆角卡片（`TaskRowStyle`），可借半透明卡片让背景透出，保持文字可读。

关键约束：
- 数据是**单一 LiteDB 文件**：备份 / 恢复、改库路径迁移都是整文件复制（ADR-0013 已确认）。方案应尽量不打破「单文件即数据」。
- 同步是**整实体 JSON payload + LWW**（ADR-0010）：`ApplySync` 对列表用远端整实体 `Upsert` 覆盖本地，**凡不在 `TaskListSync` 里的本地字段，每次同步到达都会被远端 payload 冲掉**——与附件（ADR-013）同一陷阱。
- `TaskList` 已通过 `TrackedCollection` 自动入 outbox；系统列表两层排除、永不入队。
- LiteDB 无 schema：`TaskList` 新增字段自动持久化，旧文档用默认值，无需迁移。

## 决策

### 纯色同步、图片字节本地存储
- 背景**颜色**（`#RRGGBB`）作为 `TaskList` 两个新字段（`BackgroundType` / `BackgroundColor`）进 `TaskListSync` payload，随列表正常同步、LWW 覆盖，服务端零改动（未知字段被旧设备忽略）。
- 背景**图片字节**存独立 `list_backgrounds` collection（`ILiteCollection<ListBackground>`，**不进 `TrackedCollection` / outbox**），按 `_id = listId` `Upsert` 单行，仅本机可见，与附件（ADR-013）完全同构。

### 数据模型：类型枚举 + 独立字节实体
```csharp
enum ListBackgroundType { None, Solid, Image }
// TaskList: BackgroundType + BackgroundColor("#RRGGBB")，同步
// ListBackground: Id(=listId) / ListId / Data(byte[]) / FileName，不同步
```
字节不进 `TaskItem` / `TaskList`，天然免疫整实体覆盖问题；`ApplyTombstone` 列表分支删除列表时级联删背景字节，无孤儿数据。备份 / 恢复 / 迁移全部复用单文件复制。

### 背景只铺任务区，不铺标题栏
主题背景只盖「任务列表区 + 底部输入框」（`Grid.Row="1" RowSpan="2"`），列表标题栏保持全局背景，标题文字永远清晰。搜索视图回落全局背景。两层结构：底背景 `Border` + 图片专用半透明遮罩 `Border`。

### 可读性：半透明卡片 + 图片遮罩
- 任务行 `TaskRowStyle` 背景改**半透明** `TaskCardBrush`（白/黑，Alpha 65%），hover 用 `TaskCardHoverBrush`（85%），文字仍落在卡片上、**不改文字颜色**。
- 背景为图片时叠加 `ListBackgroundMaskBrush`（白/黑 30%）压暗，保证卡片上的文字对比度。
- 三支新笔刷都是 `{DynamicResource}`，浅 / 深主题即时重染；**不动** `FluentTextBox`（详情面板共用），仅 `AddTaskBox` 局部覆盖新笔刷。

### 背景强弱（透明度）按列表单独设置、本地存储（v2）
列表主题对话框新增「背景强弱」滑杆（20%–100%），控制**背景层整体不透明度**——纯色与图片统一走一个旋钮，数值越低背景越向默认窗口背景淡出；图片的可读性遮罩不动，保证文字对比度。存独立 `list_background_settings` collection（`ListBackgroundSetting`，`_id = listId` 单行），与图片字节同为 local-only、不进 outbox：字段放 `TaskList` 会被远端整实体覆盖抹掉，且透明度是绑定本地资产（图片字节 / 本地配色）的显示偏好，跨端无意义。仅当值 ≠ 100 才落行，缺失行读回默认 100；删除列表时级联清理。

### 卡片不透明度按列表单独设置、本地存储（v3）
同一行再加入「卡片不透明度」（`CardOpacityPercent`，30%–100%，缺失读回 65，即主题原本的默认），控制任务区卡片 `TaskCardBrush` / `TaskCardHoverBrush` 的 **alpha**（RGB 不变，hover 自动 +20% 封顶 100%）。卡片笔刷是全局共享 `{DynamicResource}`，无法像背景层那样按列表各算一支笔刷，因此由 `ThemeService` 统一拥有并在三处时机按**当前激活列表**重染：切换主题（`Apply`）、切换激活列表（`OnActiveListChanged`）、主题对话框确定且编辑的是激活列表（`SetListTheme`）。两张表设置同存一行、同一个 `SetListThemeSettings` 读写，整实体列表 upsert / 墓碑删除对两者一起免疫或一起清理。

### 入口
列表头部「⋯」菜单（系统 + 自定义列表）+ 侧边栏右键菜单（自定义列表）都新增「列表主题」；对话框仿 `TagManageDialog`（色板 + HEX + 原生取色器），另含实时预览、图片选择（上限 8 MB）、移除图片、无背景、背景强弱滑杆、卡片不透明度滑杆。

## 后果
- 优点：纯色跨端同步；图片本地存储不破坏单文件模型、不碰同步层 / 服务端；删除级联清理无孤儿；预览所见即所得。
- 权衡：
  - 背景**图片不同步**，多端用户需自行传递（与附件一致，v1 有意为之）。
  - 图片字节入库使 db 文件变大；设 8 MB 上限防膨胀。
  - 图片加载走内存解码（`OnLoad` 冻结笔刷），大图会占内存；后续可做缩略图缓存。
- 未来若需同步图片：仿 ADR-013 附件结论——独立端点 / 存储按需下载，不进 JSON payload。
