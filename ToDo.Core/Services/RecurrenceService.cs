using ToDo.Models;

namespace ToDo.Services;

/// <summary>Recurring-task engine (ADR-015). Owns the pure next-due date math, the
/// close-time generation ("complete → spawn next instance"), and the cross-device
/// series dedup that keeps every series at at-most-one open instance.</summary>
public static class RecurrenceService
{
    /// <summary>Next due date strictly after <paramref name="today"/> for a rule, or null
    /// when the rule doesn't recur. Pure — no IClock dependency; callers pass their
    /// clock's Today so tests pin the boundary exactly.
    /// <para>
    /// Starts from <paramref name="currentDue"/> and advances until the due DATE is past
    /// today (a daily task finished late lands on tomorrow, a monthly one on the next
    /// occurrence). Time-of-day is carried through, so a 09:00 task stays 09:00.
    /// </para></summary>
    public static DateTime? ComputeNextDue(RecurrenceFrequency frequency, int interval, DateTime currentDue, DateTime today)
    {
        if (frequency == RecurrenceFrequency.None || interval <= 0) return null;

        var next = Advance(currentDue, frequency, interval);
        while (next.Date <= today)
            next = Advance(next, frequency, interval);
        return next;
    }

    /// <summary>Steps a due date one occurrence forward (not the full "until after today"
    /// loop — ComputeNextDue owns that). Weekdays skips straight to the next Monday from
    /// a weekend; Monthly/Yearly clamp an out-of-range day (31 → last day of month, Feb 29
    /// → Feb 28 on common years).</summary>
    private static DateTime Advance(DateTime due, RecurrenceFrequency frequency, int interval) => frequency switch
    {
        RecurrenceFrequency.Daily => due.AddDays(interval),
        RecurrenceFrequency.Weekdays => NextWeekday(due),
        RecurrenceFrequency.Weekly => due.AddDays(7 * interval),
        RecurrenceFrequency.Monthly => AddMonthsClamped(due, interval),
        RecurrenceFrequency.Yearly => AddYearsClamped(due, interval),
        _ => due,
    };

    private static DateTime NextWeekday(DateTime due)
    {
        var next = due.AddDays(1);   // AddDays keeps the time-of-day, so a 09:00 task stays 09:00
        if (next.DayOfWeek == DayOfWeek.Saturday) return next.AddDays(2);   // Sat → Mon
        if (next.DayOfWeek == DayOfWeek.Sunday) return next.AddDays(1);     // Sun → Mon
        return next;
    }

    private static DateTime AddMonthsClamped(DateTime due, int months)
    {
        var total = due.Year * 12 + (due.Month - 1) + months;
        var year = total / 12;
        var month = total % 12 + 1;
        return new DateTime(year, month, Math.Min(due.Day, DateTime.DaysInMonth(year, month)),
            due.Hour, due.Minute, due.Second);
    }

    private static DateTime AddYearsClamped(DateTime due, int years)
    {
        var day = due.Day;
        if (due.Month == 2 && day == 29 && !DateTime.IsLeapYear(due.Year + years)) day = 28;
        return new DateTime(due.Year + years, due.Month, day, due.Hour, due.Minute, due.Second);
    }

    /// <summary>Close-time generation, called from CloseTask. Handles the full three-state
    /// contract (ADR-015):
    /// <list type="bullet">
    /// <item>non-recurring → null, nothing happens;</item>
    /// <item><paramref name="endSeries"/> → clears the rule on the current instance
    /// (the caller's subsequent Update persists it), returns null;</item>
    /// <item>otherwise → creates the next instance and inserts it via the tracked
    /// collection (auto-stamped + outboxed), returning it.</item>
    /// </list>
    /// The at-most-one-open-instance guard refuses to generate when another open instance
    /// of the same series exists (e.g. a reopened, still-open earlier instance), so a
    /// single device never produces duplicates by itself.
    /// </summary>
    public static TaskItem? TryGenerateNext(DatabaseService db, TaskItem task, DateTime today, bool endSeries)
    {
        if (task.Recurrence == RecurrenceFrequency.None) return null;

        if (endSeries)
        {
            task.Recurrence = RecurrenceFrequency.None;
            task.RecurrenceInterval = 1;
            return null;
        }

        if (task.DueDate is not { } dueTs) return null;   // no due date → no next instance (UI forces one)
        var currentDue = DateTimeOffset.FromUnixTimeMilliseconds(dueTs).LocalDateTime;
        if (ComputeNextDue(task.Recurrence, task.RecurrenceInterval, currentDue, today) is not { } nextDue) return null;

        var seriesId = task.RecurrenceSeriesId ?? task.Id;
        bool hasOpenInstance = db.Tasks.Find(t =>
            t.Id != task.Id && t.CloseRecord == null
            && (t.RecurrenceSeriesId == seriesId || t.Id == seriesId)).Any();
        if (hasOpenInstance) return null;

        var next = new TaskItem
        {
            Title = task.Title,
            Note = task.Note,
            ListId = task.ListId,
            GroupId = task.GroupId,
            Order = task.Order,
            IsImportant = task.IsImportant,
            TagIds = new List<string>(task.TagIds),
            DueDate = new DateTimeOffset(nextDue).ToUnixTimeMilliseconds(),
            Reminder = ShiftReminder(task.Reminder, currentDue, nextDue),
            Steps = new System.Collections.ObjectModel.ObservableCollection<TaskStep>(
                task.Steps.Select(s => new TaskStep { Title = s.Title, Order = s.Order })),
            Recurrence = task.Recurrence,
            RecurrenceInterval = task.RecurrenceInterval,
            RecurrenceSeriesId = seriesId,
        };
        db.Tasks.Insert(next);
        return next;
    }

    /// <summary>Moves a reminder along by the same wall-clock span the due date moved,
    /// so "remind 30 min before" stays 30 min before on the next instance.</summary>
    private static long? ShiftReminder(long? reminder, DateTime currentDue, DateTime nextDue)
    {
        if (reminder == null) return null;
        return reminder + (long)(nextDue - currentDue).TotalMilliseconds;
    }

    /// <summary>Series invariant enforcement after a sync round-trip (and at startup):
    /// for each series with more than one open instance, keep the newest-ModifiedAt one
    /// and tracked-delete the rest — each delete produces a tombstone, so the dedup itself
    /// syncs to every device. Deterministic (global ModifiedAt winner), idempotent.</summary>
    public static void DedupeSeries(DatabaseService db)
    {
        var open = db.Tasks.Find(t => t.CloseRecord == null && t.Recurrence != RecurrenceFrequency.None).ToList();
        foreach (var group in open.GroupBy(t => t.RecurrenceSeriesId ?? t.Id))
        {
            var instances = group.ToList();
            if (instances.Count <= 1) continue;
            var keeper = instances.OrderByDescending(t => t.ModifiedAt).First();
            foreach (var dup in instances.Where(t => t.Id != keeper.Id))
                db.Tasks.Delete(dup.Id);
        }
    }
}
