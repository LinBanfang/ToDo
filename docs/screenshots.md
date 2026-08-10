# README 功能截图：生成与验证

README 顶部五张功能截图（[work-list.png](../screenshots/work-list.png) / [list-theme.png](../screenshots/list-theme.png) / [my-day.png](../screenshots/my-day.png) / [sticky-note.png](../screenshots/sticky-note.png) / [settings.png](../screenshots/settings.png)）由**演示数据**驱动生成，保证内容可复现、可重新截取。

## 数据来源

截图不来自真实数据，而是先由 [ToDo.Demo](../ToDo.Demo/) 生成一份演示数据库（工作 / 学习 / 生活 / 健身列表、分组、彩色标签、步骤、截止日期、My Day 任务），应用启动时指向这份临时数据库，因此每次截图内容一致，也不会触碰用户的真实 `%LOCALAPPDATA%\ToDo\todo.db`。

其中「学习」列表自带**主题背景图**（`ToDo.Demo/Assets/demo-theme-bg.jpg`，随 Demo 程序集复制到输出目录，种子时写入本地未同步集合），用于展示列表主题效果；图片仅存本机、不同步，与其他真实列表主题一致。

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
4. 启动应用，UIAutomation 驱动界面：点击侧边栏「工作」→ 截图；点击 footer 便笺按钮 → 便笺弹出，按标题（迷你便笺）定位独立置顶窗口 → 截图；便笺「返回主界面」恢复主窗 → 点击「我的一天」→ 截图；点击「学习」（自带主题背景图）→ 截 `list-theme.png`；打开设置页 → 点击左侧导航「行为」滚动到行为区块（展示任务行显示开关）→ 截图。
5. 每张截图合成到**透明画布**并画出**合成软阴影**后自动做边缘自检，失败即抛错并退出（退出码 1）：四边最外条带必须**完全透明**（合成阴影不能溢出画布边缘），且阴影入条带的深度左右、上下对称（容差 2px，抗锯齿亚像素差异）。便笺内容密集、条带取 3px。主题截图 `list-theme.png` 的背景图铺满内容区直抵右缘，四边不同色，该项检查对**它跳过**（窗口阴影已由扩展帧边界排除，无需再校验）。

## 两个关键细节（踩过的坑）

### 1. 用 `DWMWA_EXTENDED_FRAME_BOUNDS` 截取，避免窗口阴影

`GetWindowRect` 返回的矩形会把 Windows 窗口的**投影阴影**也算进去（Win10/11 实测左右下各约 7px、顶部 0），截出来四边不对称（上窄、左右下宽）。因此脚本用 DWM 的 `DWMWA_EXTENDED_FRAME_BOUNDS`（attr 9）取窗口**可见区域**截图，四边统一。

验证方式：对每张截图采样四边最外条带，要求**完全透明**（`A=0`）、阴影入条带深度左右 / 上下对称（容差 2px），保证合成阴影不溢出留白、四边渲染对称。

### 2. 截图时把窗口切成直角，避免圆角露出桌面背景

Win11 窗口默认圆角，圆角外是透明区域——即使去掉阴影，四角仍会透出桌面壁纸。脚本启动窗口后设置 DWM `DWMWA_WINDOW_CORNER_PREFERENCE`（attr 33）为 `1`（`DWMWCP_DONOTROUND`），让角落填满窗口自身颜色（标题栏 / 侧边栏色），截图干净。

验证方式：合成后 PNG 的四个角是透明画布（圆角阴影沿对角线自然淡出），圆角弧线内应为窗口自身内容色——说明截图捕获时角落填的是窗口色而非桌面壁纸。

> 注意：这只影响脚本临时启动的窗口，用户正常运行的窗口不受影响，仍是系统圆角。

### 3. 透明画布 + 合成软阴影，让截图像"浮在页面上"

窗口截图本身不含系统投影（见细节 1），直接贴到 README 上显得单薄；且窗口四边颜色不同（顶部标题栏 `#F3F2F1`、左侧边栏 `#FAF9F8`、内容区 `#FFFFFF`），任何单一画布底色都会让某条边露出一圈"框"。因此脚本把截图合成到**透明画布**上，并在窗口背后画一圈**合成软阴影**：一叠同心圆角矩形，每个的 alpha 是线性衰减剖面 `45×(1−t)` 的差，叠加后任意偏移处的总 alpha 恰为 `45×(1−t/blur)`——于是 `$blur` 就是阴影的真实可见宽度（旧方案用双三次放大实心块，可见宽度被缩放内核锁死在 ~4px，改不了）。透明底让阴影直接融进宿主页面背景：GitHub 浅色主题下是白底，深色主题下窗口直接浮在深色页面上，都不会有颜色框。

当前参数：`$blur=12`、`$alpha=45`（峰值不透明度 ~18%）、`$N=24` 圈、`$Margin=12`px 留白。扩张上限压在 `blur-2`，给最外圈抗锯齿羽化留 2px 尾巴，确保不碰到 PNG 边缘。像素采样确认左缘 alpha 从画布 `A=0` 线性递增到窗口边 `A≈40`。

## 像素级验证（截图后人工抽检）

主题色对不上、边框不对称都会让 README 截图显得突兀。主窗截图尺寸 **1210×737**（窗口 1186×713 居中，四周 12px 透明留白 + 合成阴影），便笺 **364×544**（窗口 340×520）。抽检坐标（浅色主题，指窗口内）：

| 区域 | 期望颜色 |
|---|---|
| 标题栏（顶部） | `#F3F2F1` |
| 侧边栏（左侧） | `#FAF9F8` |
| 主内容区 | `#FFFFFF` |
| 设置页背景 | `#F3F2F1` |
| 窗口四周留白 | 透明（`A=0`），阴影随偏移向窗口线性加深 |

深色主题对应：标题栏 / 内容 `#202020`，侧边栏 `#1B1B1B`，文字 `#FFFFFF`，四周留白同样透明。

上表适用于无列表主题的截图；`list-theme.png` 的右缘是背景图本身（颜色取决于图片），左缘仍是侧边栏色，不做统一色校验。
