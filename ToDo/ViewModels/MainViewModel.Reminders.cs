using System.Linq;
using ToDo.Models;

namespace ToDo.ViewModels;

public partial class MainViewModel
{
    // ─── ReminderToast actions ─────────────────────────────
    // The logic lives here (unit-testable with the injected FakeClock); the toast is a
    // thin forwarder. Snooze / Open / Complete map to the three toast buttons.

    /// <summary>Resolve a task by id: prefer the in-memory instance (the one the user is
    /// looking at), fall back to the DB (the toast may outlive a reload). Returns null
    /// when the task is gone — buttons then silently no-op (stale toast).</summary>
    private TaskItem? ResolveTask(string taskId) =>
        Tasks.FirstOrDefault(t => t.Id == taskId) ?? _db.Tasks.FindById(taskId);

    /// <summary>稍后提醒: push the reminder +10 minutes and persist. The poll loop re-fires
    /// it at the new time (rescheduling re-enters the eligible set).</summary>
    public void SnoozeReminder(string taskId)
    {
        if (ResolveTask(taskId) is not { } task) return;
        task.Reminder = _clock.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
        UpdateTask(task);
    }

    /// <summary>打开任务: select the task; the toast's code-behind also brings the main
    /// window to the front (WindowManager.ShowMain).</summary>
    public void OpenReminderTask(string taskId)
    {
        if (ResolveTask(taskId) is not { } task) return;
        SelectedTask = task;
    }

    /// <summary>完成: reuse the existing CloseTaskCommand (Complete) — recurring tasks
    /// spawn their next instance and, as a side effect, an undo bar appears (feature 1
    /// linkage; no cycle, since undoing only reopens). Already-closed tasks are ignored.</summary>
    public void CompleteReminderTask(string taskId)
    {
        if (ResolveTask(taskId) is not { } task) return;
        if (task.CloseRecord != null) return;
        CloseTaskCommand.Execute((task, CloseMode.Complete, false));
    }
}
