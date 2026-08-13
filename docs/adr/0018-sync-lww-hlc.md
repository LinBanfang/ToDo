# ADR-018: 同步 LWW 时钟权威（混合逻辑时钟 HLC）

## 状态

已采纳（已实施，v1.3.3+）

## 背景

LWW（last-writer-wins）冲突裁决以客户端生成的 `ModifiedAt`（毫秒）为准：写入时 `TrackedCollection` 用 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` 盖章；服务端 `SyncStore.Merge` 用 `change.ModifiedAt < existing.ModifiedAt` 丢弃旧写入；客户端 `ApplySync` 用 `local.ModifiedAt > remote.ModifiedAt` 保留本地新写入。

服务端 `ServerSeq` 已是单调游标（ADR-010 §2-5，免疫墙钟），但它只用于**增量拉取**，不参与冲突裁决——裁决权完全在客户端墙钟。

时钟偏移下有四个失效模式（见 ROADMAP P2-10）：

- **F1 快钟设备碾压**：设备 B 时钟快 1h，其编辑 `ModifiedAt` 恒高于真实较新的编辑，系统性赢得 LWW。
- **F2 毫秒平局歧义**：两设备同毫秒写入时，服务端 `>=`（后者覆盖）与客户端 `<=`（远端胜）裁决方向相反，第三台设备可能收敛到不同结果。
- **F3 NTP 回拨**：时钟被校正后，新写入盖的旧时间戳会被系统性丢弃，直到墙钟追上已同步的「未来」时间戳。
- **F4 本地变更误盖章**（已修复）：`DailyMyDayReset` 曾为纯本地「我的一天」变更改写 `ModifiedAt`，重推陈旧快照赢得 LWW——根因同源：把客户端墙钟当作唯一真相源。

## 决策

引入 **HLC（Hybrid Logical Clock）** 作为 `ModifiedAt` 的真相源：每台设备维护 `(physical, logical, discriminator)` 三元组，写入时推进、应用远端变更时合并，保证**因果序**下「后编辑者恒胜」，时钟偏移 / NTP 回拨 / 同毫秒平局全部确定性收敛。

### 编码

`ModifiedAt` 保持单个 `long`，编码为：

```
ModifiedAt = (physicalMs << 21) | (logical << 8) | discriminator
```

- `physicalMs`（高 43 位）：物理毫秒，覆盖到约 2248 年。
- `logical`（中 13 位）：同一物理毫秒内的单调计数器（每设备）。
- `discriminator`（低 8 位）：设备判别值，随 `DeviceId` 一次性生成并持久化（取 GUID 末字节），保证同一设备稳定、不同设备几乎不碰撞。

服务端与 LiteDB / EF / JSON **零改动**——它们只把 `ModifiedAt` 当不透明可比较整数，编码后天然按 `physical → logical → discriminator` 字典序比较。已核对全库：`ModifiedAt` 仅用于排序比较（Merge / IsLocalNewer / RecurrenceService 去重 / 搜索排序），无任何代码把它当墙钟时间显示。

### 设备侧 HLC 状态与推进

设备持久化一份 HLC 状态（随设置落盘，重启后不回退），规则：

- **写入**（`TrackedCollection._stamp` 统一入口）：
  ```
  pNow = wallClockMs
  if pNow > physical: physical = pNow; logical = 0
  else:              logical += 1            // 同毫秒或回拨
  // logical 溢出 13 位时：physical += 1; logical = 0
  ModifiedAt = encode(physical, logical, discriminator)
  ```
- **应用远端变更**（`ApplySync` / `SyncService` 拉取后）：`physical = max(physical, remotePhysical)`，`logical = max(logical, remoteLogical)`。保证本设备下一次写入严格晚于所有已见远端变更（因果序）。

### 裁决语义

- 服务端 `Merge`：`change.ModifiedAt < existing.ModifiedAt` 丢弃——编码后即「物理更晚者胜」，平局（同物理 + 同逻辑）由 `discriminator` 确定性决定，**两端收敛到同一结果**。
- 客户端 `IsLocalNewer`：`local > remote`——同一字典序，与服务端一致。

### 迁移（协议升版）

1. `SyncProtocol.Version` 1 → 2（旧客户端 / 旧服务端不互通，避免迁移期混版，沿用 ADR-010 §2-6 的「版本不符即拒绝」）。
2. 客户端首次升级：把现有实体 `ModifiedAt` 重基为 `existing << 21`（`physical=existing, logical=0, discriminator=0`），HLC 状态初始化为 `max(existing) << 21`，并 `BootstrapSync()` 全量重推（复用服务端重置路径）。
3. 服务端镜像无需迁移：`ModifiedAt` 仍是 `long`，且镜像可丢弃、客户端可重推（ADR-010 限制）。

## 后果

- **优点**：因果序正确（后编辑者恒胜）；时钟偏移 / NTP 回拨 / 同毫秒平局全部免疫；服务端零改动；`ModifiedAt` 保持单 `long`，`SyncChange` / DTO / 序列化签名不变。
- **权衡**：`ModifiedAt` 语义从「unix 毫秒」变为「编码 HLC」——任何直接把它当墙钟时间的代码需 `>> 21`（已核对无此类消费点，但新增代码须遵守）。
- **限制**：并发（因果无关）编辑仍是整实体 LWW，胜者由 `discriminator` 确定性决定——比现状（由墙钟决定）一致，但不做字段级合并 / CRDT（明确出界）。
- **残余**：`discriminator` 8 位在设备数极多时可能碰撞（2–3 台可忽略）；若未来多账户共享服务器，改由服务端分配 device ordinal。
