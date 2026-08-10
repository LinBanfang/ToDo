# ADR-015: 重复任务（recurring）

## 状态

已采纳（v1：每日 / 工作日 / 每周 / 每月 / 每年，完成时生成下一实例；系列有且仅有一个进行中实例）

## 背景

目前任务完全没有重复规则，是与 Microsoft To Do 差距最大的功能。日常使用里"每天喝水 / 每周一汇报 / 每月还信用卡"这类任务，目前只能靠手动新建，重复性劳动明显。

关键约束（与既有设计一致的既有事实）：

- **完成流**：`CloseTask` 只写 `CloseRecord` + `Completed` 并 `_db.Tasks.Update`，无任何生成逻辑；`CloseMode` 区分 Complete（完成）/ Cancel（取消）。
- **同步是整实体 JSON payload + LWW**（ADR-0010）：`ApplySync` 对任务用远端整实体 `_rawTasks.Upsert` 覆盖本地，**凡不在 `TaskSync` 里的本地字段，每次同步到达都会被远端 payload 冲掉**——重复规则字段必须进 `TaskSync` 才能存活（与 ADR-014 同一陷阱）。
- **Outbox 是最新态每实体一条**（`SyncTracker`）：写操作即打 `ModifiedAt` 并 upsert 一条 `SyncEvent`，删除记录墓碑；LWW 靠 `ModifiedAt` 决胜负。
- **`IsMyDay` / `MyDayOrder` 明确不同步**（每设备独立）；「我的一天」重置 `DailyMyDayReset` 是启动时扫描任务做增量更新的既有模式，且已用假时钟（`IClock`）可测。
- **LiteDB 无 schema**：`TaskItem` 新增字段自动持久化，旧文档读回默认值，无需迁移。
- 任务行 / 详情面板已有截止日期、提醒的完整 UI 与本地化模式可复用。

## 决策

### 数据模型：三个字段直接放 TaskItem，全部进 TaskSync

```csharp
enum RecurrenceFrequency { None = 0, Daily = 1, Weekdays = 2, Weekly = 3, Monthly = 4, Yearly = 5 }

// TaskItem 新增（与 SyncEntityTypes.Task 的 TaskSync DTO 同步新增同名字段）：
//   RecurrenceFrequency Recurrence          // 默认 None
//   int RecurrenceInterval = 1              // 每隔 N 天/周/月/年（v1 UI 固定 1，字段与日期数学预留）
//   string? RecurrenceSeriesId              // 生成的下一实例指向系列根任务的 Id；根任务为 null
```

- 重复规则是**任务语义**，必须跨设备同步，故字段直接放 `TaskItem` 并加进 `TaskSync`（`ToChange` / `FromChange` 双向映射）。旧客户端 / 旧服务端忽略未知字段，零兼容成本。
- **周一 / 每月几号 / 每年几月几号一律从实例的 `DueDate` 推导**，不额外存字段：每周 = 与当前截止日期同星期几，每月 = 同几号（越界钳制），每年 = 同月同日。这匹配"创建任务设定截止时间 → 选重复"的最常见心智。
- **重复要求有截止日期**：UI 里选中重复时若无 `DueDate`，自动把 `DueDate` 置为今天。
- `RecurrenceSeriesId` 用于把同一系列的实例串起来（系列根不变，生成实例指回根），是"系列唯一进行中实例"不变量与同步去重的依据。

### 关闭行为三态：完成 / 取消本次 / 取消定时任务

`CloseTask` 的关闭动作与生成逻辑统一走 `RecurrenceService.TryGenerateNext(task, clock)`（护栏见下）：

- **完成（勾选，mode == Complete）**：关闭当前实例并生成下一实例——系列常规推进。
- **取消本次（右键菜单，mode == Cancel + SkipOccurrence）**：关闭当前实例（灰色）并生成下一实例——"这次不做，明天照常"。
- **取消定时任务（右键菜单，mode == Cancel + EndSeries）**：关闭当前实例、**清空当前实例的重复规则**（`Recurrence = None`、`RecurrenceInterval = 1`）且不生成下一实例——系列终止；该实例成为普通一次性任务，重新打开也不会再复发。
- **非重复任务**：行为不变（仅关闭，不生成）。

生成逻辑（`TryGenerateNext`）：
1. 计算 `nextDue`（见下）；`null` 表示无规则 → 不生成。
2. **护栏**：若该系列已存在任一进行中实例（`CloseRecord == null`，`RecurrenceSeriesId == 系列 id || Id == 系列 id`），**不生成**——避免"重新打开已完成实例再完成"或同步竞态下重复生成。
3. 新建 `TaskItem`：新 GUID；复制标题 / 备注 / 列表 / 分组 / 排序 / 重要标记 / 标签 / 重复规则；`Steps` **重置为全部未完成**（新实例从零开始）；`IsMyDay` / `MyDayOrder` 不复制（每设备状态，启动时 `DailyMyDayReset` 会按截止日期自动加）；`RecurrenceSeriesId = task.RecurrenceSeriesId ?? task.Id`；`DueDate = nextDue`。
4. `Reminder = current.Reminder + (nextDue - currentDue)`——**保持提醒相对截止日期的偏移**，每日同一时间、每月同钟点都对。
5. `_db.Tasks.Insert(next)` → 走 tracked 包装，自动打 `ModifiedAt` 并入 outbox，跨设备同步。

### 下一截止日期数学：`ComputeNextDue(rule, currentDue, today)`

纯函数（可单测），无 `DateTime` 依赖网络：

1. `base = currentDue`。
2. 按规则推进一步：`next = advance(base)`：
   - Daily：`base + Interval 天`
   - Weekdays：`base + 1 天`，若落周六则 +2、周日则 +1（即跳到下周一）
   - Weekly：`base + 7*Interval 天`（保持星期几）
   - Monthly：`base 加 Interval 个月`，日号钳制（1/31 越界 → 2/28、2/29）
   - Yearly：`base 加 Interval 年`，保持月日（2/29 → 平年 2/28）
3. `while next <= today: next = advance(next)`——**下一实例必须严格晚于今天**。逾期才完成的每日任务 → 明天而非今天；每周一任务 → 下周一；每月 15 号任务本月 10 号补完成 → 本月 15 号。循环有界（每月最多 12 次，每年最多 12 次），安全。
4. `null` 场景：无规则 / 无可推日期 → 不生成。

### 同步语义：系列有且仅有一个进行中实例 + 幂等去重

多设备离线各自完成的经典竞态：A 完成 → 生成 N_A；B（未同步）也完成 → 生成 N_B；两边联网后系列出现两个进行中实例。设计以两条不变量收敛：

- **不变量**：每个系列（`RecurrenceSeriesId` 归并，根任务自身计入）**至多一个进行中实例**。
- **去重通道**：`ApplySync` 之后与启动加载时各跑一次 `DedupeSeries(db)`——按系列归并进行中实例，`ModifiedAt` 最新者存活，其余走 tracked 删除（产生墓碑，同步掉所有端的重复）。判定确定（取全局最大 `ModifiedAt`），两端收敛到同一幸存者，幂等不抖动。
- **生成护栏**（见上）保证单端本地不重复；去重通道兜底跨端竞态。这延续了项目"整实体 LWW + 收敛性可接受"的既有务实风格，完整 CRDT 级正确性不在 v1 范围内（见"后果-权衡"）。

### UI：详情面板「重复」行 + 右键关闭菜单分流

- 详情面板新增一行「重复」，复用截止日期 / 提醒的菜单模式：不重复 / 每天 / 每个工作日 / 每周 / 每月 / 每年。选中即写字段走 tracked 更新（进 outbox 同步）。
- 选中重复且无 `DueDate` 时自动置 `DueDate = 今天`。
- **重复任务的行右键菜单**在「取消」处分流为两项：「取消本次」（跳过本次、系列继续）与「取消定时任务」（终止系列）；非重复任务右键仍只有原「取消」。
- v1 不做间隔（每 2 天）与自定义星期几的 UI，字段与数学已预留。

## 后果

- 优点：
  - 规则字段进 `TaskSync`，同步语义与现有整实体 LWW 完全一致，服务端零改动。
  - 生成走 tracked `Insert`，天然进 outbox；系列不变量让多设备竞态收敛为"至多一个进行中实例"，用户看到的行为与 MS To Do 一致（一个待办，完成出下一个）。
  - 日期数学是纯函数，`RecurrenceService` 接 `IClock`，可复用现有假时钟测试体系。
  - 无 schema 迁移，旧数据读回默认 None 即不重复。
- 权衡 / 已知限制：
  - 多设备离线并发完成时，去重可能丢弃一端的生成（保留 `ModifiedAt` 更新的那个），被丢实例的个性化修改会丢失——v1 接受，文档化。
  - 步骤在下一实例重置为未完成；若用户期望继承已完成步骤，需后续调整（MS To Do 也是重置）。
  - 重复依赖 `DueDate`；无截止日期选重复会被自动补一个今天。
  - 删除任务只删该实例，不动系列其他实例。
- 未来扩展（不在 v1）：间隔 UI、自定义星期几、结束条件（`EndDate` / 次数上限）、逾期补发（一次性把错过的都生成）。

## 已确认决策（评审结论）

1. **取消拆分**：右键「取消」在重复任务上拆为「取消本次」（跳过本次、系列继续）与「取消定时任务」（终止系列）两项。
2. **日期基准**：每周 / 每月 / 每年一律从实例 `DueDate` 推导，不做显式星期 / 日期选择器。
3. **步骤重置**：下一实例步骤全部重置为未完成。
