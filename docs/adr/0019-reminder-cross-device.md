# ADR-019: 提醒跨设备一致性（持久化 fired 状态）

## 状态

已采纳（已实施，v1.4.0；依赖 ADR-018 的 LWW 修复）

## 背景

提醒去重键 `_fired` 是内存态 `HashSet<string>`（键 `taskId|reminderMs`），不持久化、不参与同步。后果（ROADMAP P2-7）：同一任务的提醒在多台设备各自独立触发——应用内卡片 + 声音 + Windows 原生 toast 各弹一次，单用户多端时重复打扰；且在某设备完成任务后，尚未同步的另一设备仍会照常触发。

## 决策

把「该提醒已触发」落为**任务的可同步字段** `FiredReminder : long?`（记录已触发过的 `Reminder` 值），跨设备共享；用持久状态替换内存 `HashSet`。

### 语义

- 触发条件：`task.Reminder != null && task.CloseRecord == null && task.FiredReminder != task.Reminder`。
- 触发时：`task.FiredReminder = task.Reminder`，经 `_db.Tasks.Update(task)`（tracked → 盖章 `ModifiedAt` + 进 outbox，随 ADR-018 的 HLC 正确同步）。
- 重新排期：`Reminder` 值变化 → `FiredReminder != Reminder` → 再次触发（正确）。
- 关闭（完成 / 取消本次）：清 `FiredReminder`，保留「重新打开 → 再次触发」的现有行为（对齐内存版 `_fired` 的 `RemoveWhere` 修剪语义）。
- 原生 toast：`NativeReminderScheduler.Reconcile` 的「未来提醒」过滤加 `FiredReminder != Reminder`，使已触发提醒的待发原生 toast 被一并清除（跨设备抑制）。

### 为什么是字段而非独立实体

每任务至多一个 `Reminder`，单字段即等价于内存里的 `(taskId, reminderMs)` 集合。相比新增 `ReminderFire` 实体类型（第 6 类同步实体 + 序列化 / ApplySync / 墓碑级联全套），字段只动 `TaskItem` + `TaskSync` + 序列化两处，blast radius 最小。代价是「触发」计为一次任务编辑（整实体重推）——单用户、低频率，可接受。

### 迁移

- `TaskSync` 增 `FiredReminder` 字段（`long?`，`WhenWritingNull` 默认省略，向后兼容）。
- 无需协议升版：新增可选字段，旧值 `null` = 未触发，语义向后兼容。
- 删除内存 `_fired`；`ReminderService` 构造函数不再预热修剪，改为每次 poll 读 `FiredReminder`。

## 后果

- **优点**：单用户多端提醒只弹一次；完成任务后跨设备不再误触发；原生 toast 同步抑制。
- **权衡**：触发产生一次可同步写（依赖 ADR-018 使该写不因时钟偏移误伤真实编辑）；`FiredReminder` 随任务整实体同步。
- **限制**：并发（两台设备几乎同时触发）时 LWW 收敛到「已触发」，至多一方多余触发一次（窗口 = 同步间隔，通常 ≤60s）；不做全局唯一触发队列。
