# ADR-010: 自托管多端同步（可选后端）

## 状态
已采纳（分阶段实施：抽 ToDo.Core → 同步元数据/outbox → 服务端 → WPF 客户端 → 未来 MAUI 安卓）

## 背景
应用原定「零后端」单机（ADR-001）。用户需要在多台设备间同步待办数据，决定**自建轻量同步服务器**部署在自有 VPS（Ubuntu + Caddy + Let's Encrypt 自动 HTTPS），安卓端未来以 MAUI 复用同一套同步引擎。

## 决策

### 同步语义
- **跨设备同步**：任务 / 列表 / 分组 / 列表分组 / 标签 / 步骤 / 重要标记 / 关闭记录。
- **每设备本地、不同步**：`IsMyDay` / `MyDayOrder`（「我的一天」，仿 Microsoft To Do）。
- **各端确定性自建、不同步**：系统列表（`IsSystem`）。`TaskList.TaskCount` 为派生值，不入同步 payload。

### 架构
- **ToDo.Core**：`net9.0` 单目标类库（WPF `net9.0-windows` 与 MAUI `net9.0-android` 均可引用），承载模型、DatabaseService、本地化、同步引擎（SyncTracker / SyncHttpClient / 序列化 / DTO），无 Windows API。
- **ToDo.Server**：.NET 9 Minimal API + EF Core + SQLite（WAL），单用户。`POST /api/sync` 单端点完成 push+pull。

### 同步协议
1. 客户端**本地优先**：任何写入经 `TrackedCollection` 拦截，写入 outbox（`sync_events`，每实体最新态 upsert）。
2. 推送：outbox 全量 → 服务器 **LWW（last-writer-wins，按 `ModifiedAt`）** 合并。
3. 拉取：服务器返回 `ServerSeq > since` 的增量变更。
4. 应用：客户端 `ApplySync`（LWW + 保留本机 My Day + 墓碑级联），`ClearPushed` 清空已推送事件。
5. 游标：**服务器单调自增 `ServerSeq`**，不用墙钟时间（免疫时钟偏差 / NTP 跳变）。
6. 版本：响应携带 `protocolVersion`（共享常量 `SyncProtocol.Version`）。不一致时客户端**拒绝应用回复**并显示红色「服务器版本不符」，避免用旧协议破坏本地数据（双方需同步升级）。

### 墓碑与级联
- 删除写 tombstone 进 outbox；远程墓碑应用时客户端做级联：列表墓碑 → 关联任务改投「任务」列表、分组清空；分组墓碑 → 任务 `GroupId=null`；标签墓碑 → 从任务 `TagIds` 剥离。

### 认证
- 共享同步密钥，`X-Sync-Key` 请求头 + SHA256 + `CryptographicOperations.FixedTimeEquals`（固定时间比较）。单用户，无账号系统。

### 并发与触发
- 客户端：`DispatcherTimer` 60s + 窗口聚焦触发 + 手动「立即同步」；`Interlocked` 重入保护；所有 LiteDB 访问集中在 UI/调度线程，HTTP 离线程。
- 服务端：`BEGIN IMMEDIATE` 事务串行化写入者。

### 部署
- 自包含 `linux-x64` 发布（VPS 无需 .NET），systemd 单元 + Caddy 反向代理，增量上传。见 [ToDo.Server/deploy/DEPLOY.md](../../ToDo.Server/deploy/DEPLOY.md)。

## 后果
- **优点**：数据跨设备可用；服务器极简（无业务逻辑，纯合并）；MAUI 直接复用 Core。
- **权衡**：整实体 LWW 单用户可接受，冲突由「输家重推 → 服务器返回更新版本」自愈。
- **限制**：
  - 单用户共享密钥，无多账户隔离。
  - 服务端用 `EnsureCreated()` 建表，**无 schema 迁移**；结构变更需重置镜像库（服务端数据可视为可丢弃镜像，客户端可重推）。
  - 服务端数据库被清空后，已同步设备不会自动重推全部数据（bootstrap 仅在 `LastSyncServerSeq == 0` 时触发）；如需恢复，可重置某设备同步游标或加一台新设备引导全量上传。
