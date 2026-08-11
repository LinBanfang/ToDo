using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo;

public partial class MainWindow
{
    // ─── Attachments (local-only, ADR-013) ──────────────────
    private const int MaxAttachmentMb = 50;
    private const long MaxAttachmentBytes = MaxAttachmentMb * 1024 * 1024L;

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        var task = ViewModel.SelectedTask;
        if (task == null) return;

        var dlg = new OpenFileDialog { Title = Loc.AddAttachment, Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;

        foreach (var file in dlg.FileNames)
            AddAttachmentFile(task, file);
        ReloadDetailAttachments();
    }

    private void AttachmentPanel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void AttachmentPanel_Drop(object sender, DragEventArgs e)
    {
        var task = ViewModel.SelectedTask;
        if (task == null || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

        foreach (var f in files)
            AddAttachmentFile(task, f);
        ReloadDetailAttachments();
    }

    private void AddAttachmentFile(TaskItem task, string filePath)
    {
        FileInfo? info = null;
        try
        {
            info = new FileInfo(filePath);
            if (info.Length > MaxAttachmentBytes)
            {
                FluentDialog.Show(this, Loc.AttachmentTooLarge(MaxAttachmentMb), Loc.Error);
                return;
            }
            App.Database!.AddAttachment(new TaskAttachment
            {
                TaskId = task.Id,
                FileName = info.Name,
                Size = info.Length,
                Data = File.ReadAllBytes(filePath),
                AddedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        }
        catch
        {
            FluentDialog.Show(this, Loc.AttachmentOpenFailed(info?.Name ?? filePath), Loc.Error);
        }
    }

    private void AttachmentOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TaskAttachment att)
        {
            try
            {
                // Extract to a unique temp file (Id prefix avoids name collisions),
                // then let the OS pick the default handler by extension.
                var dir = Path.Combine(Path.GetTempPath(), "ToDoAttachments");
                Directory.CreateDirectory(dir);
                var invalid = Path.GetInvalidFileNameChars();
                var name = new string($"{att.Id}-{att.FileName}".Select(c => invalid.Contains(c) ? '_' : c).ToArray());
                var path = Path.Combine(dir, name);
                File.WriteAllBytes(path, att.Data);
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch
            {
                FluentDialog.Show(this, Loc.AttachmentOpenFailed(att.FileName), Loc.Error);
            }
        }
    }

    private void AttachmentRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not FrameworkElement fe || fe.DataContext is not TaskAttachment att) return;
        App.Database!.DeleteAttachment(att.Id);
        ReloadDetailAttachments();
    }

    /// <summary>Re-reads the selected task's attachments from the DB into the [BsonIgnore]
    /// list the detail pane binds to, and refreshes the row paperclip count.</summary>
    private void ReloadDetailAttachments()
    {
        var task = ViewModel.SelectedTask;
        if (task == null) return;
        task.Attachments.Clear();
        foreach (var a in App.Database!.GetAttachments(task.Id))
            task.Attachments.Add(a);
        App.Database.RefreshAttachmentCounts(new[] { task });
    }

    private void DueDateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not Button btn) return;

        var today = DateTime.Today;
        var menu = new ContextMenu { PlacementTarget = btn };

        var todayItem = new MenuItem { Header = Loc.Today };
        todayItem.Click += (s, _) => SetDueDate(today);
        menu.Items.Add(todayItem);

        var tomorrowItem = new MenuItem { Header = Loc.Tomorrow };
        tomorrowItem.Click += (s, _) => SetDueDate(today.AddDays(1));
        menu.Items.Add(tomorrowItem);

        // Next week (next Monday)
        var nextMonday = GetNextMonday();
        var nextWeekItem = new MenuItem { Header = Loc.ThisWeek };
        nextWeekItem.Click += (s, _) => SetDueDate(nextMonday);
        menu.Items.Add(nextWeekItem);

        menu.Items.Add(new Separator());

        var pickItem = new MenuItem { Header = Loc.PickDate };
        pickItem.Click += (s, _) =>
        {
            var dialog = new Views.Dialogs.DateTimeDialog(
                ViewModel.SelectedTask!.DueDate ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            { Owner = this, Title = Loc.Date };
            if (dialog.ShowDialog() == true && dialog.Saved)
            {
                SetDueDate(DateTimeOffset.FromUnixTimeMilliseconds(dialog.ResultTimestamp).LocalDateTime.Date);
            }
        };
        menu.Items.Add(pickItem);

        if (ViewModel.SelectedTask.DueDate != null)
        {
            menu.Items.Add(new Separator());
            var removeItem = new MenuItem { Header = $"✕  {Loc.Delete}" };
            removeItem.Click += (s, _) =>
            {
                ViewModel.SelectedTask!.DueDate = null;
                ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
                RefreshDetailPickers();
            };
            menu.Items.Add(removeItem);
        }

        menu.IsOpen = true;
    }

    private void SetDueDate(DateTime date)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.DueDate = new DateTimeOffset(date).ToUnixTimeMilliseconds();
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

    private void ReminderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not Button btn) return;

        var menu = new ContextMenu { PlacementTarget = btn };
        var now = DateTime.Now;

        foreach (var (label, offset) in new[] {
            ("1 " + (Loc.Language == AppLanguage.Chinese ? "小时后" : "hour later"), 1.0),
            ("3 " + (Loc.Language == AppLanguage.Chinese ? "小时后" : "hours later"), 3.0),
            (Loc.Tomorrow + " 9:00", (now.Date.AddDays(1).AddHours(9) - now).TotalHours),
            (Loc.ThisWeek + " 9:00", (GetNextMonday().AddHours(9) - now).TotalHours),
        })
        {
            var item = new MenuItem { Header = label };
            var ts = DateTimeOffset.UtcNow.AddHours(offset).ToUnixTimeMilliseconds();
            item.Click += (_, _) => SetReminder(ts);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());

        var pickItem = new MenuItem { Header = Loc.PickDate };
        pickItem.Click += (_, _) =>
        {
            // includeTime: reminders carry a time of day (due dates intentionally do not).
            var dlg = new Views.Dialogs.DateTimeDialog(
                ViewModel.SelectedTask!.Reminder ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                includeTime: true)
            { Owner = this, Title = "Reminder" };
            if (dlg.ShowDialog() == true && dlg.Saved)
                SetReminder(dlg.ResultTimestamp);
        };
        menu.Items.Add(pickItem);

        if (ViewModel.SelectedTask.Reminder != null)
        {
            menu.Items.Add(new Separator());
            var removeItem = new MenuItem { Header = $"✕  {Loc.Delete}" };
            removeItem.Click += (_, _) => ReminderClear_Click(sender, e);
            menu.Items.Add(removeItem);
        }
        menu.IsOpen = true;
    }

    private static DateTime GetNextMonday()
    {
        var today = DateTime.Today;
        var daysUntil = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntil == 0) daysUntil = 7;
        return today.AddDays(daysUntil);
    }

    private void SetReminder(long ts)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.Reminder = ts;
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

    private void ReminderClear_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.Reminder = null;
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

    private void RecurrenceBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null || sender is not Button btn) return;

        var menu = new ContextMenu { PlacementTarget = btn };
        foreach (var (freq, header) in new[] {
            (RecurrenceFrequency.None, Loc.RepeatNone),
            (RecurrenceFrequency.Daily, Loc.RepeatDaily),
            (RecurrenceFrequency.Weekdays, Loc.RepeatWeekdays),
            (RecurrenceFrequency.Weekly, Loc.RepeatWeekly),
            (RecurrenceFrequency.Monthly, Loc.RepeatMonthly),
            (RecurrenceFrequency.Yearly, Loc.RepeatYearly),
        })
        {
            var item = new MenuItem { Header = header };
            var f = freq;
            item.Click += (_, _) => SetRecurrence(f);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void SetRecurrence(RecurrenceFrequency freq)
    {
        if (ViewModel.SelectedTask is not { } task) return;
        task.Recurrence = freq;
        // Recurring tasks need a due date to schedule the next instance (ADR-015):
        // picking a rule without one backdates it to today, so generation has an anchor.
        if (freq != RecurrenceFrequency.None && task.DueDate == null)
            task.DueDate = new DateTimeOffset(DateTime.Today).ToUnixTimeMilliseconds();
        ViewModel.UpdateTaskCommand.Execute(task);
        RefreshDetailPickers();
    }

    private void DetailDueDate_Clear(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedTask == null) return;
        ViewModel.SelectedTask.DueDate = null;
        ViewModel.UpdateTaskCommand.Execute(ViewModel.SelectedTask);
        RefreshDetailPickers();
    }

}
