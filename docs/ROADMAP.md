# To Do — 路线图

> 长期规划文档，只记录**尚未完成**的工作；已实现的功能以 [CHANGELOG.md](../CHANGELOG.md) 为准。优先级会随项目状态调整。

---

## 一、工程质量与可维护性

### 1. MainViewModel 测试覆盖

`MainViewModel`（约 945 行、约 40 个命令）目前零测试。`RefreshActiveTasks` 的过滤 / 排序 / 分组、`CountForList`、`DailyMyDayReset`、级联删除均无覆盖——这是用户每天都会触发的逻辑，且只依赖 `DatabaseService`，可测性很好。唯一阻碍是 `DailyMyDayReset` 直接用 `DateTime.Today`，无时钟注入。

- [ ] 注入时钟（IClock），让时间相关逻辑可测
- [ ] 为上述核心逻辑补单元测试

### 2. 抽取 ReorderService

`MainWindow.xaml.cs`（约 1685 行）里散落 5 份复制粘贴的「半区插入排序」实现：`ReorderListGroups`、`ReorderSidebarList`、`TaskRow_Drop`、`ReorderTaskGroups`、`StepRow_Drop`。

- [ ] 抽出公共 `ReorderService` + 测试
- [ ] 借此削减 code-behind 债务

### 3. 同步诊断日志接线

同步失败目前只把状态点变灰，`SyncDiagnostics.Log` 是未接线的空实现；对照 `UpdateService` 每步都写 `DiagnosticLog`，同步这块完全没接上。

- [ ] 同步全流程（出站 / 入站 / 冲突 / 失败）接入 `DiagnosticLog`

---

## 二、功能演进（对标 Microsoft To Do）

### 4. 重复任务（recurring）

目前完全没有，是与 MS To Do 差距最大的功能。

- [ ] 每日 / 工作日 / 每周 / 每月等重复规则
- [ ] 完成一次后按规则生成下一次；与提醒、同步模型结合
- [ ] 覆盖重复提醒场景

### 5. 撤销完成 / 删除

删除是永久性的，无撤销。

- [ ] 完成 / 删除操作后出现撤销入口（如 MS To Do 的 toast 撤销）

### 6. 键盘快捷键

目前仅 Ctrl+N / Esc。

- [ ] 常用操作快捷键：新建任务 / 搜索 / 列表切换 / 完成任务等，完善键盘可达性

### 7. 提醒交互增强

- [ ] ReminderToast 增加操作按钮：稍后提醒 / 打开任务 / 完成
- [ ] 应用未运行、到点未触发时的补发策略（当前启动时仅静默标记，不补弹）
- [ ] 可选：改用 Windows 原生 toast notification（现为应用内 Fluent 卡片）

### 8. 自定义铃声完善

铃声目前仅支持单个 wav 文件（基于 `SoundPlayer`）。

- [ ] 支持 mp3 / m4a / ogg 等格式（需引入解码或播放库）
- [ ] 内置铃声库（多首可选），不止「默认叮咚 + 自定义文件」二选一
- [ ] 音量控制 / 试听时长

### 9. 提醒跨设备一致性

提醒去重键（`_fired`）是内存态，不参与同步；多设备场景下提醒触发 / 抑制可能不一致。

- [ ] 评估提醒状态持久化或进入同步模型

---

## 三、平台扩展（远期）

### 10. MAUI 安卓端

ADR-010 已规划；`ToDo.Core` 按可复用设计，同步协议带版本号，移动端可直接复用。

- [ ] 评估并启动移动端客户端（工程量最大，建议放在其余各项之后）
