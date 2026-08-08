# 更新日志

本项目遵循[语义化版本](https://semver.org/lang/zh-CN/)。格式参照 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)。

## [v1.2.0] - 2026-08-08

### 新增
- **迷你便笺 + 系统托盘常驻**：主界面 footer 图标或托盘右键呼出始终置顶的速览便笺（无边框圆角卡片），展示当前列表任务，可完成任务 / 切换列表 / 恢复已完成任务；头部「返回主界面」恢复主窗、「关闭」回到托盘（见 [ADR-012](docs/adr/0012-sticky-note-tray.md)）
- **托盘菜单**：打开主界面 / 迷你便笺 / 退出（真正的退出路径），改用应用内置 Fluent 菜单样式（圆角、主题化、随浅色 / 深色主题即时切换）；关闭主窗口不再退出、默认最小化到托盘
- **行为设置区**：「关闭主窗口时最小化到托盘」「在便笺中显示标签」两个开关
- **便笺头部优化**：列表名可拖动移位，仅 ∨ 按钮展开列表下拉；「已完成」折叠箭头移至文字左侧
- **便笺标签**：任务行显示彩色标签小 pill（可设置关闭）

### 修复
- 主窗隐藏（托盘 / 便笺模式）时后台更新检查的对话框无可见 owner

### 优化
- 便笺任务行截止日期与标签同行显示；主窗口便笺按钮改为手写便笺图标（QuickNote）
- 设置页数据库路径以 `%LOCALAPPDATA%` / `%USERPROFILE%` 形式显示，避免在界面与截图里暴露系统用户名

## [v1.1.2] - 2026-08-08

### 新增
- **标题栏 / 边框颜色跟随主题**：窗口标题栏由 DWM 着色，深色主题 → 深色标题栏 + 浅色文字，浅色主题 → 浅色标题栏 + 深色文字（Windows 10/11，见 [ADR-011](docs/adr/0011-titlebar-theme.md)）
- **演示数据生成工具 ToDo.Demo**：灌入展示各功能示例的数据库，用于生成 README 截图与试用
- **删除确认等对话框统一为 Fluent 风格**，颜色跟随浅色 / 深色主题

### 修复
- 运行时切换主题后标题栏 / 边框颜色残留
- 侧边栏未分组列表拖动排序不生效
- 分组内列表任务数 0 时也显示，与系统 / 未分组列表逻辑统一
- 侧边栏列表图标列加宽到 36px，拉开图标与文字间距

### 优化
- 收窄 FluentTextBox 左内边距（10 → 6），减少输入文字左侧空白
- 更新 README 功能截图（工作列表 / 我的一天 / 设置页）

## [v1.1.1] - 2026-08-08

### 修复
- 修复自建同步服务器部署的 4 个根因（VPS 部署）；部署脚本强制 LF 行尾，防止 Windows autocrlf 在 VPS 上导致 CRLF bug
- 部署文档去敏感表述，补齐部署中踩过的坑

### 优化
- 任务列表标题与最上任务间距增加 15px
- 侧栏同步图标改为自绘粗线宽矢量图形，视觉更清晰

## [v1.1.0] - 2026-08-08

### 新增
- **多端同步（可选，自建后端）**：`ToDo.Server`（.NET 9 Minimal API + EF Core + SQLite），多设备间同步任务 / 列表 / 分组 / 标签 / 步骤 / 重要标记；「我的一天」保留在本设备、不同步（见 [ADR-010](docs/adr/0010-self-hosted-sync.md)）
- 抽取 `ToDo.Core` 共享库（模型 / DatabaseService / 本地化 / 同步引擎），WPF 与未来 MAUI 复用
- 主界面侧边栏同步状态图标 + 设置页同步状态着色
- 服务端版本不符检测（响应携带 `protocolVersion`，不一致时客户端拒绝应用回复）

### 安全
- 同步认证：共享密钥 `X-Sync-Key` 请求头 + SHA256 + 固定时间比较；增量上传与部署目标本地化

## [v1.0.15] - 2026-08-05

### 新增
- 测试项目（ToDo.Tests / ToDo.Server.Tests）与 CI 构建门禁

### 修复
- 可空性告警与英文日期区域格式

## [v1.0.14] - 2026-08-05

### 新增
- 设置页单页锚点导航，新增「关于」区块
- MIT 许可（[LICENSE](LICENSE)）

## [v1.0.13] - 2026-08-05

### 新增
- 诊断日志系统（零依赖本地日志，写入 `<exe>\logs\app.log`，更新检查接入，见 [docs/logging.md](docs/logging.md)）

### 修复
- 更新源无数据时给出描述性报错（指明具体源），不再落入晦涩的 `MissingFieldException`

## [v1.0.12] - 2026-08-05

### 修复
- 手动检查更新展示真实错误与最新版本号（失败弹首个真实网络错误）

## [v1.0.11] - 2026-08-05

### 新增
- 标签支持自定义颜色（色板 + HEX 输入 + 原生取色器）
- 内嵌设置页（主内容区页面切换，见 [ADR-008](docs/adr/0008-in-app-settings-page.md)）
- 架构决策记录（ADR）体系（见 [docs/adr/](docs/adr/)）

## [v1.0.10] - 2026-08-05

### 新增
- 首次启动自动创建 settings.json 并预置 GitHub + Gitee 更新源

## [v1.0.3] - [v1.0.9] - 2026-08-05

### 内部
- 自动发布流程完善：GitHub Actions 发布 + Gitee 同步（多步 CI 修复，见 [ADR-005](docs/adr/0005-auto-release-ci.md)）

## [v1.0.2] - 2026-08-05

### 新增
- **自动更新**：vendored AutoUpdater.NET，支持多个更新源（GitHub / Gitee / 私有 appcast），有新版时全自动下载、替换并重启（见 [ADR-006](docs/adr/0006-auto-update-vendored.md)）
- 更新源可通过 settings.json 配置

## [v1.0.1] - 2026-08-05

### 修复
- 任务提醒到点弹系统通知（此前为已知限制，见 [ADR-007](docs/adr/0007-reminder-notification.md)）
- 完成深色主题覆盖
- 大任务集下削减热路径开销

## [v1.0.0] - 2026-07-29

### 初始版本
仿 Microsoft To Do 的 Fluent Design 风格 WPF 待办应用：

- 列表管理（系统列表 + 自定义列表 + 列表分组、折叠、拖拽排序 / 归类）
- 待办项：子步骤（行内编辑 / 拖拽排序 / 完成 / 一键升级为任务）、自定义彩色标签、截止日期 / 提醒、重要标记
- 双重关闭（勾选 = 完成，右键 = 取消）、任务详情面板
- 全局搜索（跨所有列表按标题 / 备注实时过滤）
- 浅色 / 深色主题（DynamicResource 运行时切换）、中英文切换
- LiteDB 本地存储（默认 `%LOCALAPPDATA%\ToDo\todo.db`，路径可配置）

[unreleased]: https://github.com/LinBanfang/ToDo/compare/v1.1.2...HEAD
[v1.1.2]: https://github.com/LinBanfang/ToDo/compare/v1.1.1...v1.1.2
[v1.1.1]: https://github.com/LinBanfang/ToDo/compare/v1.1.0...v1.1.1
[v1.1.0]: https://github.com/LinBanfang/ToDo/compare/v1.0.15...v1.1.0
[v1.0.15]: https://github.com/LinBanfang/ToDo/compare/v1.0.14...v1.0.15
[v1.0.14]: https://github.com/LinBanfang/ToDo/compare/v1.0.13...v1.0.14
[v1.0.13]: https://github.com/LinBanfang/ToDo/compare/v1.0.12...v1.0.13
[v1.0.12]: https://github.com/LinBanfang/ToDo/compare/v1.0.11...v1.0.12
[v1.0.11]: https://github.com/LinBanfang/ToDo/compare/v1.0.10...v1.0.11
[v1.0.10]: https://github.com/LinBanfang/ToDo/compare/v1.0.9...v1.0.10
[v1.0.9]: https://github.com/LinBanfang/ToDo/compare/v1.0.8...v1.0.9
[v1.0.8]: https://github.com/LinBanfang/ToDo/compare/v1.0.7...v1.0.8
[v1.0.7]: https://github.com/LinBanfang/ToDo/compare/v1.0.6...v1.0.7
[v1.0.6]: https://github.com/LinBanfang/ToDo/compare/v1.0.5...v1.0.6
[v1.0.5]: https://github.com/LinBanfang/ToDo/compare/v1.0.4...v1.0.5
[v1.0.4]: https://github.com/LinBanfang/ToDo/compare/v1.0.3...v1.0.4
[v1.0.3]: https://github.com/LinBanfang/ToDo/compare/v1.0.2...v1.0.3
[v1.0.2]: https://github.com/LinBanfang/ToDo/compare/v1.0.1...v1.0.2
[v1.0.1]: https://github.com/LinBanfang/ToDo/compare/v1.0.0...v1.0.1
[v1.0.0]: https://github.com/LinBanfang/ToDo/releases/tag/v1.0.0
