# ADR-006: 应用自动更新（vendored AutoUpdater.NET + 多源）

## 状态
已采纳

## 背景
应用已有 GitHub 自动发布，需要应用内"检查新版本 → 提示 → 下载 → 替换 → 重启"的完整闭环，并支持 GitHub / Gitee / 私有服务器多种源。

## 决策
- **Vendor AutoUpdater.NET 1.9.3 核心**到 `ToDo/Updater/`（保留 `AutoUpdaterDotNET` 命名空间与 MIT 许可，`LICENSE` 随附；删除 WinForms UI / WebView2 / resx，宿主用自己的 WPF 对话框）。
- 启动后按序尝试多个更新源（settings.json `UpdateSources`，空则回退 GitHub + Gitee 默认），第一个成功生效；类型支持 `github`/`gitee`（JSON）与 `appcast`（AutoUpdater.NET XML）。
- 更新对话框：立即更新（下载 zip → 写隐藏 PowerShell 更新脚本（路径 base64）→ 应用退出 → 脚本等待进程结束、解压到临时目录、覆盖安装目录、重启）、以后再说（2 天）、跳过此版本。
- 首次启动自动创建 settings.json 并预置两个源。

## 后果
- 优点：完整自动更新闭环；多源容灾（国内网络切 Gitee）。
- 权衡：依赖 PowerShell 脚本完成替换（便携应用需可写安装目录）；不能后台静默（退出才替换）；vendor 库与上游同步需手动。
