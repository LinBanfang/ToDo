# ADR-007: 提醒到点通知（托盘 NotifyIcon + 定时查询）

## 状态
已采纳

## 背景
`Reminder` 字段一直只记录提醒时间并展示，到点不弹任何通知——这是早期文档记录的"已知限制"。

## 决策
- 新增 `ReminderService`：`DispatcherTimer` 每 15 秒用 LiteDB `Reminder` 索引查询到期提醒（`Reminder <= now && CloseRecord == null`），第一次到点通过托盘 `NotifyIcon` 弹系统通知并播放提示音。
- 启动时预标记已到期的提醒，避免开机轰炸。
- 为使用 `NotifyIcon`（WinForms 组件），csproj 开启 `UseWindowsForms`，并把 `System.Windows.Forms`/`System.Drawing` 从隐式 using 移除避免与 WPF 类型冲突。

## 后果
- 优点：真实的系统通知 + 声音；索引查询后台开销小。
- 权衡：仅在应用运行期间触发（退出后不推送）；托盘图标常驻。
