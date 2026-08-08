# ADR-012: 迷你便笺 + 系统托盘常驻

## 状态
已采纳

## 背景
应用默认 `OnLastWindowClose`（关窗即退出），`NotifyIcon` 已在 `ReminderService` 中常驻（仅用于提醒气泡、无右键菜单）。用户希望：通过主界面 footer 图标或托盘右键呼出一个**始终置顶**的迷你便笺窗口，展示当前列表任务，可完成任务、可切换列表；主窗口 X 改为「最小化到托盘」。

目标：把「常驻托盘图标」这项已付的成本变成真实功能——速览便笺 + 托盘菜单 + 托盘常驻生命周期；同时不破坏现有提醒气泡、设置项持久化与主题机制。

## 决策

### 窗口模式切换（WindowManager）
- 新增静态协调器 `WindowManager`：持有便笺单例、`IsQuitting` 标志、编排主窗 / 便笺 / 托盘三者切换。任一时刻最多一个窗口可见：`OpenSticky()` 隐藏主窗并显示便笺；`ShowMain()` 关闭便笺并恢复主窗。
- **单实例守卫**：`OpenSticky()` 检查便笺 `IsLoaded`，已打开则仅 `Activate()`；便笺 `Closed` 后引用置空，绝不重复 `new`。
- **关闭语义**：便笺关闭（点 X）**始终回到托盘**（主窗保持隐藏）；主窗只通过便笺「返回主界面」按钮、托盘双击或托盘菜单「打开主界面」恢复。因此不再需要「关闭便笺后回到托盘」设置项（初版设计有，定稿时移除）。

### 便笺窗口自身（StickyWindow）
- **无边框卡片**：`WindowStyle="None"` + `WindowChrome`（`CaptionHeight=0`、`ResizeBorderThickness=5`）——无系统标题栏 / 最小化 / 最大化按钮，边缘仍可拖拽缩放；Win11 上经 DWM `DWMWA_WINDOW_CORNER_PREFERENCE` 圆角（`TitleBarService.RoundCorners`）。头部拖拽用 `DragMove()`，落在列表切换器 / 按钮上的按压不触发拖拽。
- **自定义头部**：左侧 = 列表切换器，**列表名文本不触发下拉，是拖拽把手**，仅右侧 ∨ 按钮（`ToggleButton`）展开下拉（`Popup` + `ListBox`，选后即收起），保证便笺可拖动移位；右侧 = 「返回主界面」（Home 字形）+「关闭」（X 字形）两个按钮。列表名与下方任务区用分割线隔开。
- **不设 `Owner`**：若 `Owner = MainWindow`，主窗 `Hide()` 会连带隐藏 owned 窗口，破坏「主窗隐藏、便笺可见」的模式切换。置顶靠 `Topmost=True`，`ShowActivated=False` 防抢焦点，`ShowInTaskbar=False` 不占任务栏。
- **共享 `ActiveListId`**：便笺内切换列表写的是同一个 VM 的 `ActiveListId`，重开主窗时活动列表一致——这是有意设计（单一选中态），不是两套独立选中。
- **只读 + 完成任务**：可完成任务（复选框 → `CloseTaskCommand`）、已完成区块可点击重新打开（`ReopenTaskCommand`）；不做编辑、不显示详情。交互复选框沿用 MainWindow 的可点击 Border 模式，避免 `IsChecked` 双向绑定与 `TaskItem.Completed` 双重切换。已完成区块用自定义 `ToggleButton` 折叠（默认收起，含计数与旋转箭头），不复用系统 Expander 外观。
- **标签展示**：活动任务行显示彩色标签**小 pill**（复用主窗的 `TagIdsToTags` 转换器与 `StringToAlphaBrush`/`StringToBrush`，但缩小字号与内边距），便于速览时按颜色辨认任务所属域；由设置项 `StickyShowTags`（行为区，默认开）控制显隐。主窗里已经通过带名字的 pill 学会"颜色 → 标签"映射，便笺只触发记忆，故不显示标签文字。已完成行保持极简，不显示标签。
- **几何持久化**：位置 / 尺寸节流写入（`DispatcherTimer` ~300ms），关闭时兜底保存；恢复时夹取到 `SystemParameters.VirtualScreen` 内，防拔掉外接屏后窗口落屏外。`double?` 作哨兵（`System.Text.Json` 对 `NaN` 会抛异常）。

### 托盘图标与菜单（TrayService）
- 新增 `TrayService` **独占** `NotifyIcon`：在 `App.OnStartup` 创建、`OnExit` 恰好销毁一次；`ReminderService` 改为共享该图标、移除自己的创建 / 销毁，避免双重释放。
- **菜单**：WPF `ContextMenu`（不手绘 WinForms 样式），自动继承应用全局 Fluent 菜单样式——圆角、主题化、随浅色 / 深色主题即时切换，与主窗内右键菜单外观一致。菜单项：打开主界面 / 迷你便笺 / 退出；左键双击托盘图标 → 打开主界面。从 WinForms 托盘事件打开需 `Dispatcher.BeginInvoke(Input)` 延后一帧（否则 WPF 弹出立刻被托盘右键点击"吃"掉而关闭），并以 `PlacementMode.MousePoint` + 可见窗口作 `PlacementTarget` 提供 PresentationSource。
- 图标销毁后定时器已停，不会再 `ShowBalloonTip` 打到已销毁的图标。

### 应用生命周期（OnExplicitShutdown）
- `ShutdownMode="OnExplicitShutdown"`：唯一退出路径是托盘「退出」→ `Application.Current.Shutdown()`；否则关窗即进程常驻托盘。
- `MinimizeToTrayOnClose`（默认开）控制主窗 X：开 → 隐藏到托盘；关 → 保持旧的「点 X 即退出」语义（显式 `Shutdown()`）。
- `SessionEnding` 订阅 `WindowManager.Quit()`：`OnExplicitShutdown` 下 Windows 注销 / 关机时能干净退出（不用等 WPF 默认行为）。

### 对话框 owner 解析
- `WindowManager.ResolveDialogOwner()`：便笺可见 → 便笺；否则主窗不可见时先 `Show()` 再返回。主窗隐藏（托盘 / 便笺模式）时后台更新检查弹对话框也能拿到可见 owner。

## 后果
- 优点：托盘常驻从「只为提醒」变成完整功能（速览 + 菜单 + 生命周期）；模式切换对称、单实例、无焦点抢占；便笺即开即用的置顶速览卡片。
- 权衡：
  - `ShowActivated=False` 的置顶窗口首次点击可能被「激活」动作吞掉（需一次点击预热）——可接受，**不要**在 Show 时 `Activate()` 来修。
  - 无边框窗口丢失系统 DWM 阴影，便笺呈「平卡」外观（1px 描边 + 圆角）——对便笺定位可接受，留作 Phase 2 加自定义阴影。
  - Win10 无 DWM 圆角，便笺四角为直角（Win11 22000+ 才圆角）。
  - 便笺切换列表会连带改变主窗活动列表（共享选中态）——有意设计。
  - 主窗 X 语义变为「最小化到托盘」是行为变更，已做成设置开关（默认开），用户可关回旧语义。
