# ADR-003: 主题切换用 DynamicResource + 运行时字典替换

## 状态
已采纳

## 背景
早期主题按钮绑定 `ToggleTheme` 只改一个字符串，无任何 UI 消费。`StaticResource` 在启动时就把刷对象缓存进样式，运行时换主题字典不会更新已加载的样式。

## 决策
- 所有主题刷引用从 `{StaticResource X}` 改为 `{DynamicResource X}`。
- `ThemeService` 在运行时替换 `FluentColors` 字典：浅色从 XAML 字典加载，深色在代码中构建（键与浅色完全一致）。
- 替换时用引用追踪代码构建的深色字典（其 `Source` 为 null，无法按 URI 定位，否则切回浅色会失败）。
- 选择持久化到 settings.json，启动时 `App.OnStartup` 恢复。

## 后果
- 优点：切换即时生效，无需重建窗口；动态资源统一驱动全部主题刷。
- 权衡：代码中的深色调色板与 XAML 浅色调色板需保持键同步；少数语义色（完成/逾期）由转换器按当前主题解析。
