# ADR-002: 就地更新刷新模型

## 状态
已采纳

## 背景
早期每个任务命令都 `LoadTasks()`（全量重读 DB + 重建 `Tasks` 集合）+ `RefreshActiveTasks()`，而步骤操作用 `ObservableCollection` 实时更新——两套路径并存，且 `CreateTask` 曾漏掉把新任务加入内存集合（任务刷新后不显示）。

## 决策
统一为**就地更新 + 派生视图重建**：
- 任务级变更直接修改内存 `Tasks` 中的实例，再调用 `RefreshActiveTasks()`（不重读 DB）。
- 新增/删除任务显式同步 `Tasks` 集合（`Add`/`Remove`）。
- 无法自通知的派生属性在命令中显式通知：`NotifyTagsChanged()` / `NotifyCloseDisplay()` / `NotifyCompletedStepCount()`。
- 步骤级变更靠 `ObservableCollection` 实时更新（避免步骤编辑时集合重建丢焦点）。
- 全量重载只保留在同步点：`LoadAll`（启动）、`Refresh`（外部拖放）、`OnActiveListChanged`（切换列表）、列表/分组级命令。

## 后果
- 优点：每次操作不重读 DB；`SelectedTask` 保持同一实例，详情面板编辑不丢焦点。
- 权衡：派生属性需手动维护通知（漏一处 UI 即不刷新）；要求所有任务变更都经过 ViewModel 命令（外部直接改 DB 需走 `Refresh()` 全量同步）。
