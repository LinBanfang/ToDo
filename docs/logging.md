# 诊断日志系统设计方案

## 1. 目标与范围

跨机器排查问题难是痛点：更新检查在 1.0.11 曾把一切失败原因吞掉、只显示 `MissingFieldException` 的晦涩文案，用户机器上的真实原因（GitHub 403 限流 / 网络不通 / 源数据缺字段）无从得知。需要一个**轻量、零依赖、易读、写失败不影响主流程**的本地诊断日志系统。

- **首期范围**：更新检查（每个源的尝试结果 + 检查结论）。
- **预留扩展**：提醒触发、数据库启动/迁移、设置加载、启动流程。

## 2. 设计原则

| 原则 | 说明 |
|---|---|
| 零第三方依赖 | 不引 Serilog/NLog，自写静态类（符合项目"零外部依赖"风格，见 ADR-001） |
| 追加写 + 即时 flush | 每行写入即落盘，崩溃不丢日志 |
| 写失败静默 | 日志异常绝不抛给主流程 |
| 线程安全 | 更新检查跑后台线程，需 `lock` 串行化 |
| 文件有上限 | 避免无限增长，超限轮转 |
| 不记录敏感数据 | 不打任务/数据库内容；URL 若含凭据做脱敏 |

## 3. 日志文件

| 项 | 值 |
|---|---|
| 位置 | `<应用目录>\logs\app.log`（exe 同路径 `logs` 文件夹，`AppDomain.CurrentDomain.BaseDirectory`） |
| 格式 | 纯文本，一行一条 |
| 单文件上限 | 1 MB |
| 轮转 | 超限 → 当前文件改名为 `app.log.1`，新建 `app.log`；只保留最近 2 份（`app.log` + `app.log.1`） |

**权衡**：日志位于安装目录内，应用自动更新覆盖安装目录时旧日志可能被清除（诊断日志可接受，新版启动即重建）；若应用被放在不可写目录（如 `Program Files`），写日志会失败——按 5.1 静默降级，日志缺失不影响功能。

## 4. 日志格式

```
[2026-08-05 15:00:01.123] [INFO]  [update] 开始检查更新（手动触发）
[2026-08-05 15:00:02.004] [INFO]  [update] github https://api.github.com/... -> HTTP 200, version=1.0.12, zip=https://github.com/...zip
[2026-08-05 15:00:02.901] [WARN]  [update] gitee https://gitee.com/... -> 响应缺少 zip 资产（assets 为空）
[2026-08-05 15:00:03.100] [WARN]  [update] github https://api.github.com/... -> 异常：Response status code 403 (Forbidden)
[2026-08-05 15:00:03.101] [ERROR] [update] 检查失败：所有更新源均不可用，最后错误：... 403 ...
```

`[时间戳(yyyy-MM-dd HH:mm:ss.fff)] [级别] [模块] 消息`

级别：`INFO` / `WARN` / `ERROR`（首期不提供级别过滤，全量记录）。

## 5. 实现

### 5.1 新文件：`Services/DiagnosticLog.cs`

与 `SettingsService` 同风格的静态类：

```csharp
public static class DiagnosticLog
{
    // 目录 <应用目录>\logs\app.log
    public static void Info(string module, string message);
    public static void Warn(string module, string message);
    public static void Error(string module, string message);
}
```

内部实现要点：
- `lock` 保证多线程（后台更新线程 + UI 线程）串行写。
- `StreamWriter(append: true, autoFlush: true)` 或写前校验长度 + `File.AppendAllText`。
- 写前检查文件大小，超 1 MB 做轮转（改 `.1`、重建新文件、删更旧）。
- 全部 try/catch 静默（含目录创建失败）。

### 5.2 接入点：`UpdateService`

| 位置 | 记录内容 |
|---|---|
| `CheckForUpdates` / `CheckForUpdatesNow` | 检查开始（后台 / 手动触发） |
| `ParseUpdateInfo`（逐源） | 每个源的**成功结果**（HTTP/版本/zip）或**失败原因**（异常类型+消息 / 缺字段） |
| `OnUpdateChecked` | 检查结论（有新版 / 无新版+最新版本 / 失败+原因） |
| `RefreshSources` | 源列表（脱敏后的 URL） |

### 5.3 脱敏

更新源 URL 若含 `access_token=` 或 `://user:password@` 形式，写入日志前打码为 `***`。其余 URL 原样记录（用于定位是哪个源）。

## 6. 决策点

1. **级别过滤**：首期不提供配置，全量记录（文件小、低频写入，无需过滤）。后期需要可加"只记 WARN+"开关。
2. **UI 入口**：建议二期在设置页"数据"节加"打开日志目录"按钮（`Process.Start` 打开 `logs` 文件夹），方便用户取日志反馈。
3. **轮转保留份数**：2 份（`app.log` + `app.log.1`）足够诊断；不需要压缩。
4. **是否加密/敏感字段**：不打数据库内容；URL 脱敏见 5.3。

## 7. 后续扩展

- 其他子系统接入同一日志：`ReminderService`（提醒触发）、`DatabaseService`（启动/迁移）、`App.OnStartup`（设置加载、恢复流程）。
- 设置页"打开日志目录"入口（决策点 2）。
- 若未来需要结构化，再评估是否引入第三方（当前不必要）。

## 8. 实施状态与计划

### 阶段 1 — 日志设施 + 更新检查接入 ✅ 已实施（随 1.0.13 发布）

- `Services/DiagnosticLog.cs`：静态类，写入 `<exe>\logs\app.log`，1 MB 轮转保留 2 份，`lock` 线程安全，全部 try/catch 静默（目录不可写时降级到 `%TEMP%`）。
- `UpdateService` 已接入 5.2 全部点位：源列表（脱敏）、检查开始（启动/手动）、逐源结果（成功 `version/zip` 或真实异常）、检查结论。
- **实测验证** `logs/app.log`：
  - 成功：`github https://api.github.com/... -> version=1.0.12, zip=https://...ToDo-v1.0.12.zip` → `startup check: no update (latest 1.0.12)`
  - 失败：`[WARN] github http://127.0.0.1:1/... -> AggregateException: ... 由于目标计算机积极拒绝` → `[ERROR] check failed: all update sources unavailable` → `[WARN] startup check failed: 由于目标计算机积极拒绝`

### 阶段 2 — 收尾（待办）
- 设置页"数据"节加"打开日志目录"按钮（`Process.Start` 打开 `logs` 文件夹）。
