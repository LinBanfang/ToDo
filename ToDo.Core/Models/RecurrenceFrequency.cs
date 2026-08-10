namespace ToDo.Models;

/// <summary>Recurrence rule of a task (ADR-015). None = one-off task; everything
/// else schedules a next instance on close. Interval (every N days/weeks/…) rides
/// in <see cref="TaskItem.RecurrenceInterval"/>; v1 UI fixes it at 1.</summary>
public enum RecurrenceFrequency
{
    None = 0,
    Daily = 1,
    Weekdays = 2,
    Weekly = 3,
    Monthly = 4,
    Yearly = 5,
}
