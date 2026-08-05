# To Do

仿 Microsoft To Do 的 Fluent Design 风格待办事项桌面应用，基于 WPF (.NET 9) 开发。

## 功能

- **列表管理** — 系统列表（我的一天 / 重要 / 计划内 / 任务）+ 自定义列表，支持列表分组、分组折叠
- **子步骤** — 每个待办项可添加步骤，支持行内编辑、拖拽排序、完成状态、一键升级为任务
- **自定义标签** — 可创建带颜色的标签，右键菜单或详情面板分配
- **双重关闭** — 勾选 = 完成（绿色），右键 = 取消（灰色斜线），均记录关闭时间且可编辑
- **截止日期 / 提醒** — 快捷菜单选日期（今天/明天/下周/自定义）和提醒时间；提醒到点弹系统通知并响铃
- **搜索** — 跨所有列表按标题 / 备注实时过滤
- **拖放操作** — 待办项拖动排序（上半/下半半区插入）、移入 / 移出分组、移到其他列表；侧边栏列表拖动排序 / 归类
- **中英文切换** — 侧边栏底部一键切换，全界面（含弹窗、转换器）本地化
- **浅色 / 深色主题** — 侧边栏一键切换，即时生效并自动记忆
- **本地存储** — LiteDB 嵌入式数据库，数据文件默认在 `%LOCALAPPDATA%\ToDo\todo.db`，路径可在设置中变更并自动迁移
- **自动更新** — 启动时按序检查多个更新源（GitHub / Gitee / 私有 appcast），发现新版本一键自动下载、替换并重启（基于 vendored AutoUpdater.NET，MIT）

## 运行

```bash
cd ToDo
dotnet run
```

## 技术栈

| 层面 | 选型 |
|---|---|
| 框架 | WPF (.NET 9) |
| MVVM | CommunityToolkit.Mvvm |
| 数据库 | LiteDB |
| 样式 | 原生 WPF Fluent Design 自定义模板 |
| 本地化 | 静态字符串 + 窗口重建 |

## 项目结构

```
ToDo/
├── Models/          数据模型（TaskItem / TaskList / TaskGroup / ListGroup / Tag / TaskStep / CloseRecord）
├── ViewModels/      MVVM 视图模型
├── Views/Dialogs/   对话框窗口（TagManageDialog / DateTimeDialog / DbPathDialog）
├── Services/        数据库 & 本地化 & 设置
├── Converters/      值转换器
└── Styles/          Fluent 主题样式
```

## 设计文档

- 详细设计方案：[docs/design.md](docs/design.md)
- 架构决策记录（ADR）：[docs/adr/](docs/adr/)
