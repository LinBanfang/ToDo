# To Do

仿 Microsoft To Do 的 Fluent Design 风格待办事项桌面应用，基于 WPF (.NET 9) 开发。

## 功能

- **列表管理** — 系统列表（我的一天 / 重要 / 计划内 / 任务）+ 自定义列表，支持分组
- **自定义标签** — 可创建带颜色的标签，右键或详情面板分配
- **双重关闭** — 勾选 = 完成（绿色），右键 = 取消（灰色斜线），均记录关闭时间
- **截止日期** — 类似 MS To Do 的快捷菜单选日期（今天/明天/下周/自定义）
- **拖放操作** — 拖动待办项排序、移入/移出分组、移到未分组
- **中英文切换** — 侧边栏底部一键切换
- **本地存储** — LiteDB 嵌入式数据库，数据文件在程序目录

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
├── Models/          数据模型
├── ViewModels/      MVVM 视图模型
├── Views/Dialogs/   对话框窗口
├── Services/        数据库 & 本地化
├── Converters/      值转换器
└── Styles/          Fluent 主题样式
```

## 设计文档

详见 [docs/design.md](docs/design.md)
