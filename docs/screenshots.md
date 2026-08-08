# README 功能截图：生成与验证

README 顶部三张功能截图（[work-list.png](../screenshots/work-list.png) / [my-day.png](../screenshots/my-day.png) / [settings.png](../screenshots/settings.png)）由**演示数据**驱动生成，保证内容可复现、可重新截取。

## 数据来源

截图不来自真实数据，而是先由 [ToDo.Demo](../ToDo.Demo/) 生成一份演示数据库（工作 / 学习 / 生活 / 健身列表、分组、彩色标签、步骤、截止日期、My Day 任务），应用启动时指向这份临时数据库，因此每次截图内容一致，也不会触碰用户的真实 `%LOCALAPPDATA%\ToDo\todo.db`。

## 如何重新生成

仓库内脚本 [tools/screenshots/capture-screenshots.ps1](../tools/screenshots/capture-screenshots.ps1) 完成全流程：

```powershell
# 默认：Debug 构建 → 演示数据 → 浅色主题 → 写入 screenshots/
powershell -NoProfile -ExecutionPolicy Bypass -File tools/screenshots/capture-screenshots.ps1

# 深色主题截图，输出到自定义目录
powershell -NoProfile -ExecutionPolicy Bypass -File tools/screenshots/capture-screenshots.ps1 -Theme Dark -OutputDir C:\tmp\shots

# 已构建过，跳过编译
powershell -NoProfile -ExecutionPolicy Bypass -File tools/screenshots/capture-screenshots.ps1 -SkipBuild
```

脚本流程：

1. `dotnet build ToDo.slnx`（可 `-SkipBuild` 跳过）。
2. 运行 ToDo.Demo 生成临时演示 DB（`%TEMP%\todo-screenshot-<Theme>.db`）。
3. 备份真实 `settings.json`，写入临时设置（指向演示 DB、指定主题、关闭更新 / 同步 / 提醒）——**结束必恢复**，真实数据全程不被修改。
4. 启动应用，UIAutomation 驱动界面：点击侧边栏「工作」→ 截图；点击「我的一天」→ 截图；打开设置页 → 截图。
5. 每张截图自动做边缘自检，失败即抛错并退出（退出码 1）。

## 两个关键细节（踩过的坑）

### 1. 用 `DWMWA_EXTENDED_FRAME_BOUNDS` 截取，避免窗口阴影

`GetWindowRect` 返回的矩形会把 Windows 窗口的**投影阴影**也算进去（Win10/11 实测左右下各约 7px、顶部 0），截出来四边不对称（上窄、左右下宽）。因此脚本用 DWM 的 `DWMWA_EXTENDED_FRAME_BOUNDS`（attr 9）取窗口**可见区域**截图，四边统一。

验证方式：对每张截图采样四边最外 12px 条带，要求无暗色（<40）像素、且四边边缘色一致（应为当前主题边框色，浅色 `#F3F2F1`）。

### 2. 截图时把窗口切成直角，避免圆角露出桌面背景

Win11 窗口默认圆角，圆角外是透明区域——即使去掉阴影，四角仍会透出桌面壁纸。脚本启动窗口后设置 DWM `DWMWA_WINDOW_CORNER_PREFERENCE`（attr 33）为 `1`（`DWMWCP_DONOTROUND`），让角落填满窗口自身颜色（标题栏 / 侧边栏色），截图干净。

验证方式：采样四角 10×10 区域，要求无暗色像素、角点颜色等于窗口内容色。

> 注意：这只影响脚本临时启动的窗口，用户正常运行的窗口不受影响，仍是系统圆角。

## 像素级验证（截图后人工抽检）

主题色对不上、边框不对称都会让 README 截图显得突兀。抽检坐标（浅色主题，窗口 1186×713）：

| 区域 | 期望颜色 |
|---|---|
| 标题栏（顶部） | `#F3F2F1` |
| 侧边栏（左侧） | `#FAF9F8` |
| 主内容区 | `#FFFFFF` |
| 设置页背景 | `#F3F2F1` |
| 四边边缘 | `#F3F2F1`（与标题栏一致） |

深色主题对应：标题栏 / 内容 `#202020`，侧边栏 `#1B1B1B`，文字 `#FFFFFF`。
