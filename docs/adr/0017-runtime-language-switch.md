# ADR-017: 运行时语言切换（重建窗口）

## 状态

已采纳（v1：语言切换即时生效——接线 `Loc.LanguageChanged`，切换时重建主窗口并刷新托盘菜单与设置分区标题；`RestartToApply` 提示改为「即时生效」）

## 背景

ADR-016 把字符串迁入 RESX 后，语言切换仍是**重启生效**：XAML 的 `{x:Static services:Loc.X}` 在窗口**加载时**解析，切换语言不会刷新任何已解析的绑定；`Loc.LanguageChanged` 生产代码零订阅。主题能原地生效是因为用了 `{DynamicResource}`（[ADR-0003](0003-theme-dynamicresource.md)）——字符串没有对应的「动态资源」机制，`{x:Static}` 绑定无法刷新。

长命窗口只有两个，都绑定 `App.ViewModel` 单例：MainWindow（启动建一次）+ StickyWindow（每次打开重建）。设置页是 MainWindow 内嵌 overlay，重建主窗口即覆盖设置 UI。`ShutdownMode=OnExplicitShutdown`——关掉最后一个窗口不退出进程，窗口交换安全。

## 决策

### 切换路径

1. 设置页「常规」语言 ComboBox → `GeneralSection.Language` setter 持久化后调用 `Loc.SetLanguage(...)`（`"English"` → `AppLanguage.English`，否则 Chinese）。
2. `Loc.SetLanguage` 同步触发 `LanguageChanged` → `App.OnLanguageChanged` 用 `Dispatcher.BeginInvoke` 延迟一拍执行 `WindowManager.RebuildForLanguageChange()`——让 ComboBox 选中事件先收尾，避免事件中途动视觉树。
3. `RebuildForLanguageChange`：`App.Tray?.Refresh()`（托盘提示 + 菜单重解析）→ 捕获主窗口几何与可见性 → 关掉 sticky（防御）→ `IsRebuilding=true` 下 `_main.Close()`（绕过 `MainWindow.OnClosing` 的 Cancel-Hide）→ `App.CreateMainWindow()` 恢复几何 → 重指 `_main` / `Application.Current.MainWindow` → 按原可见性 Show。**不重调 `Init`**——托盘事件处理器是静态方法读 `_main` 字段。

### 为什么不做 markup extension

`LocExtension : MarkupExtension` 订阅 `LanguageChanged` 可避免重建（无闪烁、保留滚动位置），但需要：126 处 `{x:Static}` 机械替换 + 弱事件防泄漏 + 新增 `Loc.Get(key)` 访问器，且**仍要**处理托盘菜单与设置分区标题——它们不是 XAML 绑定，是构造时的 C# 捕获。blast radius 大得多；重建一条路径兜住所有 `x:Static` 过期。

### 单例 VM 的过期字符串

重建后 `App.ViewModel`（含 `SettingsViewModel`）存活 → 构造时捕获的 Loc 字符串会过期，仅两处：
- `SettingsViewModel` 8 个分区 `Title`（`{Binding Title}`）：改为 observable 属性，`Loc.LanguageChanged` 时重赋。
- `ReminderSection.ToastOptions`（卡片时长下拉标签）：`RefreshToastOptions()` 重解析。

其余 VM（MainViewModel / SyncSection）均为用时计算，无需处理；瞬态窗口（ReminderToast / FluentDialog / 原生 toast）每次创建时读 Loc，不在重建范围。

### 不做的（明确出界）

- 保留设置页滚动位置（切换后回到顶部；用户正停在「常规」分区，影响可忽略）。
- 保留瞬态 UI（撤销条等，5s 槽，可接受丢失）。
- 删除 `RestartToApply` resx 键（golden 测试钉死，保留但停用）。

## 后果

- 优点：
  - 一条重建路径兜住全部 127 处 `{x:Static}`，消费面零改动。
  - 窗口几何、`IsSettingsMode`、已选列表/任务等随 VM 单例与几何捕获保留。
  - 托盘菜单/提示、设置分区标题、卡片时长下拉同步切换。
- 权衡 / 已知限制：
  - 切换有短暂窗口重建（闪烁），设置页滚动位置回到顶部。
  - 重建触发一次 `OnActivated → Sync.Trigger()`（副作用良性，数据顺带刷新）。
