# ADR-001: 本地嵌入式存储（LiteDB）

## 状态
已采纳

## 背景
应用是单机桌面待办工具，无需后端服务，但需要可靠的持久化、简单部署、零外部依赖。候选方案包括：JSON/XML 文件、SQLite、LiteDB。

## 决策
采用 **LiteDB 嵌入式 NoSQL** 作为存储。5 张集合：`lists` / `groups` / `tasks` / `tags` / `listgroups`，模型对象直接 CRUD。数据库路径默认 `%LOCALAPPDATA%\ToDo\todo.db`，可经 `DbPathDialog` 配置并自动迁移。

## 后果
- 优点：零部署、对象直存、轻量。
- 权衡：单进程模型（App 仅自己访问）；大数据量下全表查询需靠索引（已为 `ListId/GroupId/IsMyDay/IsImportant/DueDate/Reminder` 建索引）。
- 数据导出/迁移依赖 `SettingsService` 的文件复制，无版本化 schema 迁移。

## 后续更新
本 ADR 的「无需后端服务」仅指**本地持久化选型**（LiteDB 仍作为嵌入式存储）。多端同步引入了可选的**自托管后端**（ToDo.Server），见 [ADR-010](0010-self-hosted-sync.md)；该决策不推翻本地存储选型，两者并存。
