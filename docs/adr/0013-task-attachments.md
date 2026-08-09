# ADR-013: 任务详情附件（本地存储，不同步）

## 状态
已采纳（v1：本地存储 + 详情面板管理 + 行指示；字节存 LiteDB，不同步）

## 背景
任务详情面板目前支持标题 / 截止日期 / 我的一天 / 提醒 / 步骤 / 标签 / 关闭信息 / 备注。用户需要在任务上挂文件（截图、文档等）。

关键约束：
- 数据是**单一 LiteDB 文件**：备份 / 恢复、设置页改库路径迁移都是整文件复制（`DatabaseService.ExportTo`）。方案应尽量不打破「单文件即数据」。
- 同步是**整实体 JSON payload + LWW**（ADR-0010）：服务端是纯合并的极简镜像库，无迁移、无业务逻辑。`ApplySync` 对 Task 用远端整实体 `Upsert` 覆盖本地，仅特殊保留 My Day 两字段——**不同步的字段一旦挂在 TaskItem 上，每次同步到达都会被远端 payload 冲掉**。
- 中英文走 `LocalizationService`；详情面板是统一的 `Border + StackPanel` 分段结构；任务行有「显示备注图标」等行为开关先例（`ShowTaskNote`）。

## 决策

### 范围：本地存储、不同步
附件 **只存在于添加它的设备上**，不进同步 payload，服务端零改动。沿用「每设备本地、不同步」的 My Day 先例（ADR-0010），避免给极简同步服务器加文件子系统（上传 / 下载端点、blob 存储、大小配额）。

### 数据模型：独立实体 + 独立 collection
新增 `TaskAttachment`，存**独立** `task_attachments` collection，**不作为 TaskItem 的属性**：
- 同步层根本不认识它，天然免疫整实体覆盖问题（`ApplySync` 的 `Upsert` 碰不到它）；`TaskSync` / `SyncEntitySerializer` 一行不改。
- 任务删除时级联删附件文档即可，无孤儿数据。
- `task_attachments` 用普通 `ILiteCollection`，**不进 `TrackedCollection` / outbox**。

### 字节存储：存进 LiteDB（BsonBinary）
字节以 `Data(byte[])` 随文档入库：
- 备份、恢复、改库路径迁移全部**零改动**（还是拷一个文件）。
- 删除任务即级联删除附件，无磁盘孤儿文件。
- 代价：db 文件变大；打开文件需先抽到临时目录。设**单文件上限 50 MB** 防 db 膨胀。

### 打开方式
点击附件 → 字节抽取到 `%TEMP%` 下的临时文件 → `Process.Start`（由系统默认程序打开）。不落库持久路径。

### UI
- 详情面板「备注」上方新增「附件」分段，复用现有 `Border + StackPanel` 卡样式：标题 + 「添加附件」按钮（`OpenFileDialog`）+ `ItemsControl` 列表（文件名 / 大小 / 添加时间 / 移除按钮）+ 点击打开 + **拖拽文件到面板添加**（`AllowDrop` + `Drop`）。
- 任务行显示**回形针指示图标**（仿现有备注图标）；给 `TaskItem` 加 `[BsonIgnore] AttachmentCount`，在任务加载 / 附件变更时刷新，仅作显示用、不持久化。
- 行为开关区新增 `ShowTaskAttachments`（默认开），对齐现有 `ShowTaskNote`。

### 删除级联
任务删除的三条路径都清理该任务附件：详情面板删除、列表删除、同步墓碑 `ApplyTombstone` 中 `_rawTasks.Delete`（远端删任务时清理本机孤儿附件）。

## 后果
- 优点：不破坏单文件数据模型；同步层 / 服务端零改动；无孤儿文件；备份恢复迁移全复用。
- 权衡：
  - 附件**不同步**，多端用户需自行传递文件。
  - db 文件随附件增长；单文件 50 MB 上限限制大文件附件。
  - 打开是「抽取到临时目录」，编辑原文件不会回写 db（有意设计，v1 只做读取打开）。
- 未来若需同步附件：作为独立阶段，文件走独立端点 / 存储，**不进 JSON payload**（避免 payload 爆炸与 SQLite 承载大 blob）；元数据与字节分离设计，届时加「元数据同步 + 字节按需下载」。
