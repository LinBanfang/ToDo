# ADR-011: 标题栏 / 边框跟随应用主题（DWM 着色）

## 状态
已采纳

## 背景
应用已有 Light / Dark 主题系统（ADR-003：`{DynamicResource}` + 运行时字典替换），但窗口标题栏是 **OS 绘制的**，只跟随 Windows 系统主题。应用切到深色、Windows 系统仍是浅色时，标题栏保持浅色，与深色界面割裂。全应用 6 个窗口（MainWindow + FluentDialog / UpdateDialog / TagManageDialog / DbPathDialog / DateTimeDialog）全部使用系统默认标题栏，无 `WindowChrome`、无 `AllowsTransparency`。

目标：标题栏 / 边框自动跟随应用的 Light / Dark 主题（不新增独立设置项），深色 → 深色标题栏 + 浅色文字，浅色 → 浅色标题栏 + 深色文字。

## 决策

- 新建 `TitleBarService`，用 DWM `DwmSetWindowAttribute` P/Invoke 为窗口着色：
  - **Win10 1809+（含 Win11）**：`DWMWA_USE_IMMERSIVE_DARK_MODE`（attr 20）设沉浸式暗色标志——主题 Dark 为 1，Light 为 0。
  - **Win11 22000+**：额外设 `DWMWA_CAPTION_COLOR`（34）/ `DWMWA_TEXT_COLOR`（35）/ `DWMWA_BORDER_COLOR`（36），精确对齐应用背景色与前景文字色。用 `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)` 判定，旧系统仅用沉浸式暗色模式。
- **颜色从当前主题字典取**（单一事实来源，不硬编码副本）：`Application.Current.TryFindResource("AppBackgroundBrush")` / `"TextPrimaryBrush"` 转 COLORREF（`0x00BBGGRR`：`B << 16 | G << 8 | R`）；取不到时回退与 FluentColors 相同的硬编码值。
- **挂钩主题切换**：`ThemeService.Apply` 字典交换逻辑末尾调用 `TitleBarService.ApplyAll()`，覆盖全部调用点（启动 / 设置页切换 / 侧边栏切换）。
- **各窗口着色时机**：构造函数注册 `SourceInitialized += (_, _) => TitleBarService.Apply(this)`，在 HWND 创建时着色一次。对话框是模态的，打开期间主题不可切换，无需订阅后续切换。

## 后果
- 优点：标题栏与界面永远同色，深 / 浅主题均验证通过（标题栏 #202020 / #F3F2F1 与主题背景一致、文字对比度正常）；运行时切换即时生效（含残留 bug 修复）。
- 权衡：
  - `DwmGetWindowAttribute` 无法读回 caption / text / border 颜色（返回 `E_INVALIDARG`），验证依赖截图像素分析。
  - 精确颜色仅在 Win11 生效；Win10 用沉浸式暗色模式，标题栏跟随系统暗色而非应用背景色。
  - 颜色依赖主题字典中 `AppBackgroundBrush` / `TextPrimaryBrush` 两个键，未来改色板需保证这两个键存在。
