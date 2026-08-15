using CommunityToolkit.Mvvm.Input;
using ToDo.Models;
using ToDo.Plugin.Abstractions;
using ToDo.Plugins;
using ToDo.Services;

namespace ToDo.ViewModels;

public partial class MainViewModel
{
    // ─── Task Commands ────────────────────────────────────
    [RelayCommand]
    private void CreateTask(string title)
    {
        if (ActiveList == null) return;

        // Searching → inbox; system lists → inbox; custom lists → to that list
        var listId = IsSearching
            ? "list-tasks"
            : ActiveList.Type == ListType.Custom ? ActiveList.Id : "list-tasks";
        var isMyDay = !IsSearching && ActiveList.Type == ListType.MyDay;

        var task = new TaskItem
        {
            Title = title,
            ListId = listId,
            IsMyDay = isMyDay,
            MyDayOrder = isMyDay ? NextOrder(Tasks.Where(t => t.IsMyDay).Select(t => t.MyDayOrder)) : -1,
            Order = NextOrder(Tasks.Where(t => t.ListId == listId).Select(t => t.Order)),
        };
        _db.Tasks.Insert(task);
        Tasks.Add(task); // keep the in-memory collection in sync for in-place refresh
        RefreshActiveTasks();
        _events.RaiseTaskCreated(DtoMapper.ToTask(task));
    }

    /// <summary>插件门面用的全字段创建：草稿字段显式给出（不依赖当前激活列表），其余语义与
    /// <see cref="CreateTask(string)"/> 一致（tracked Insert 盖 HLC + outbox）。</summary>
    public TaskItem CreateTaskFromDraft(NewTaskDraft draft)
    {
        var listId = string.IsNullOrWhiteSpace(draft.ListId) ? "list-tasks" : draft.ListId;
        var task = new TaskItem
        {
            Title = draft.Title,
            Note = draft.Note,
            ListId = listId,
            GroupId = draft.GroupId,
            DueDate = draft.DueDate,
            IsImportant = draft.IsImportant,
            TagIds = new List<string>(draft.TagIds ?? Array.Empty<string>()),
            Order = NextOrder(Tasks.Where(t => t.ListId == listId).Select(t => t.Order)),
        };
        _db.Tasks.Insert(task);
        Tasks.Add(task);
        RefreshActiveTasks();
        _events.RaiseTaskCreated(DtoMapper.ToTask(task));
        return task;
    }

    [RelayCommand]
    private void UpdateTask(TaskItem task)
    {
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        RefreshActiveTasks();
        _events.RaiseTaskChanged(DtoMapper.ToTask(task));
    }

    [RelayCommand]
    private void DeleteTask(TaskItem task)
    {
        // Undo snapshot: deep-copy the task + attachments before they're destroyed, so
        // undoing restores them exactly. Must be taken before DeleteAttachmentsForTask
        // clears the rows; GetAttachments returns fresh objects each call.
        var attachments = _db.GetAttachments(task.Id);
        var snapshot = task.Clone();

        _db.Tasks.Delete(task.Id);
        _db.DeleteAttachmentsForTask(task.Id);   // local attachments die with the task (ADR-013)
        Tasks.Remove(task);
        if (SelectedTask?.Id == task.Id)
            SelectedTask = null;

        RefreshActiveTasks();
        _events.RaiseTaskDeleted(task.Id);

        PushUndo(Loc.UndoDeleteMsg(snapshot.Title), () =>
        {
            _db.Tasks.Insert(snapshot);           // same id re-insert → outbox tombstone replaced
            foreach (var a in attachments)
                _db.AddAttachment(a);             // original id / filename / bytes / AddedAt
            Tasks.Add(snapshot);
            RefreshActiveTasks();                 // custom lists sort by Order → lands in place
            _events.RaiseTaskRestored(DtoMapper.ToTask(snapshot));
        });
    }

    [RelayCommand]
    private void MoveTaskToList((TaskItem task, TaskList targetList) param)
    {
        param.task.ListId = param.targetList.Id;
        param.task.GroupId = null;
        param.task.Order = NextOrder(Tasks.Where(t => t.ListId == param.targetList.Id).Select(t => t.Order));
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
        _events.RaiseTaskChanged(DtoMapper.ToTask(param.task));
    }

    [RelayCommand]
    private void MoveTaskToGroup((TaskItem task, TaskGroup? group) param)
    {
        param.task.GroupId = param.group?.Id;
        // Append at the end of the target group (ungrouped = null) so the moved task
        // lands predictably, matching MoveTaskToList's next-order placement.
        param.task.Order = NextOrder(Tasks.Where(t => t.GroupId == param.group?.Id).Select(t => t.Order));
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);

        // Auto-expand the target group so the moved task is visible
        if (param.group != null && param.group.Collapsed)
        {
            param.group.Collapsed = false;
            _db.Groups.Update(param.group);
        }

        RefreshActiveTasks();
        _events.RaiseTaskChanged(DtoMapper.ToTask(param.task));
    }

    // ─── Closing System ───────────────────────────────────
    // endSeries distinguishes the recurring-task "cancel the whole series" action
    // (cancel this occurrence = endSeries false) from a plain cancel (ADR-015).
    [RelayCommand]
    private void CloseTask((TaskItem task, CloseMode mode, bool endSeries) param)
    {
        param.task.CloseRecord = new CloseRecord
        {
            ClosedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CloseMode = param.mode,
        };
        param.task.Completed = param.mode == CloseMode.Complete;
        param.task.FiredReminder = null;   // closing clears the fired marker → reopening fires again (ADR-019)
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCloseDisplay();

        // Recurring-task generation (ADR-015): completing or cancel-this-occurrence spawns
        // the next instance; cancel-the-series clears the rule (persisted by the Update
        // below) and spawns nothing. The tracked Insert auto-stamps + outboxes it.
        var next = RecurrenceService.TryGenerateNext(_db, param.task, _clock.Today, endSeries: param.endSeries);
        if (next != null)
            Tasks.Add(next); // keep the in-memory collection in sync for in-place refresh

        _db.Tasks.Update(param.task);
        RefreshActiveTasks();

        // 完成/取消 → 各自事件；重复任务自动生成的下一实例单独发 TaskCreated（D7）。
        if (param.mode == CloseMode.Complete) _events.RaiseTaskCompleted(DtoMapper.ToTask(param.task));
        else _events.RaiseTaskCanceled(DtoMapper.ToTask(param.task));
        if (next != null) _events.RaiseTaskCreated(DtoMapper.ToTask(next));

        // Undo: only completing offers an undo bar (Cancel / endSeries don't). Undoing
        // also deletes the generated next instance, restoring the single open instance.
        if (param.mode == CloseMode.Complete)
        {
            var completedTask = param.task;
            var generated = next;
            PushUndo(Loc.UndoCompleteMsg(completedTask.Title), () =>
            {
                ReopenTask(completedTask);          // clears CloseRecord/Completed + persists
                if (generated != null)
                {
                    _db.Tasks.Delete(generated.Id); // idempotent: returns false if already gone
                    Tasks.Remove(generated);
                    RefreshActiveTasks();
                }
            });
        }
    }

    [RelayCommand]
    private void ReopenTask(TaskItem task)
    {
        task.CloseRecord = null;
        task.Completed = false;
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        task.NotifyCloseDisplay();
        _db.Tasks.Update(task);
        RefreshActiveTasks();
        _events.RaiseTaskReopened(DtoMapper.ToTask(task));
    }

    [RelayCommand]
    private void EditCloseTime((TaskItem task, long newTime) param)
    {
        if (param.task.CloseRecord == null) return;
        param.task.CloseRecord.ClosedAt = param.newTime;
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
        _events.RaiseTaskChanged(DtoMapper.ToTask(param.task));
    }

    // ─── My Day ───────────────────────────────────────────
    [RelayCommand]
    private void ToggleMyDay(TaskItem task)
    {
        if (task.IsMyDay)
        {
            task.IsMyDay = false;
            task.MyDayOrder = -1;
        }
        else
        {
            task.IsMyDay = true;
            task.MyDayOrder = NextOrder(Tasks.Where(t => t.IsMyDay).Select(t => t.MyDayOrder));
        }
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        RefreshActiveTasks();
        _events.RaiseTaskChanged(DtoMapper.ToTask(task));
    }

    // ─── Importance ───────────────────────────────────────
    [RelayCommand]
    private void ToggleImportant(TaskItem task)
    {
        task.IsImportant = !task.IsImportant;
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _db.Tasks.Update(task);
        RefreshActiveTasks();
        _events.RaiseTaskChanged(DtoMapper.ToTask(task));
    }

    // ─── Steps ────────────────────────────────────────────
    [RelayCommand]
    private void AddStep((TaskItem task, string title) param)
    {
        param.task.Steps.Add(new TaskStep
        {
            Title = param.title,
            Order = param.task.Steps.Count,
        });
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
        _events.RaiseTaskChanged(DtoMapper.ToTask(param.task));
    }

    /// <summary>Insert a new step after the given index and set it to editing mode</summary>
    public void InsertStepAfter(TaskItem task, int afterIndex)
    {
        for (int i = afterIndex + 1; i < task.Steps.Count; i++)
            task.Steps[i].Order++;
        task.Steps.Insert(afterIndex + 1, new TaskStep
        {
            Title = "",
            Order = afterIndex + 1,
            IsEditing = true
        });
        task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        task.NotifyCompletedStepCount();
        _db.Tasks.Update(task);
        _events.RaiseTaskChanged(DtoMapper.ToTask(task));
    }

    [RelayCommand]
    private void ToggleStep((TaskItem task, TaskStep step) param)
    {
        param.step.Completed = !param.step.Completed;
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
        _events.RaiseTaskChanged(DtoMapper.ToTask(param.task));
    }

    [RelayCommand]
    private void DeleteStep((TaskItem task, TaskStep step) param)
    {
        param.task.Steps.Remove(param.step);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
        _events.RaiseTaskChanged(DtoMapper.ToTask(param.task));
    }

    [RelayCommand]
    private void PromoteStepToTask((TaskItem task, TaskStep step) param)
    {
        var newTask = new TaskItem
        {
            Title = param.step.Title,
            ListId = param.task.ListId,
            Order = NextOrder(Tasks.Where(t => t.ListId == param.task.ListId).Select(t => t.Order)),
        };
        _db.Tasks.Insert(newTask);
        Tasks.Add(newTask); // keep the in-memory collection in sync for in-place refresh
        param.task.Steps.Remove(param.step);
        param.task.ModifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        param.task.NotifyCompletedStepCount();
        _db.Tasks.Update(param.task);
        RefreshActiveTasks();
        _events.RaiseTaskCreated(DtoMapper.ToTask(newTask));
        _events.RaiseTaskChanged(DtoMapper.ToTask(param.task));
    }

}
