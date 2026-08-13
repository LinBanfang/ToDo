using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;
using System.Linq;

namespace ToDo.Models;

public partial class TaskItem : ObservableObject, IOrdered
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private string _listId = string.Empty;

    [ObservableProperty]
    private string? _groupId;

    [ObservableProperty]
    private int _order;

    [ObservableProperty]
    private bool _isImportant;

    [ObservableProperty]
    private bool _isMyDay;

    [ObservableProperty]
    private int _myDayOrder = -1;

    [ObservableProperty]
    private long? _dueDate;

    [ObservableProperty]
    private long? _reminder;

    /// <summary>Value of Reminder that has already fired (ADR-019); synced so a reminder
    /// doesn't re-fire on another device. null = hasn't fired its current reminder.</summary>
    [ObservableProperty]
    private long? _firedReminder;

    /// <summary>Recurrence rule (ADR-015); None = one-off task. Synced — must be, or
    /// sync's whole-entity overwrite would wipe it (the ADR-014 trap).</summary>
    [ObservableProperty]
    private RecurrenceFrequency _recurrence = RecurrenceFrequency.None;

    /// <summary>Every N days/weeks/months/years (ADR-015). v1 UI fixes 1; field + date math reserved.</summary>
    [ObservableProperty]
    private int _recurrenceInterval = 1;

    /// <summary>Id of the series root; generated instances point back to it. The root's
    /// own value is null. Basis of the at-most-one-open-instance invariant + sync dedup.</summary>
    [ObservableProperty]
    private string? _recurrenceSeriesId;

    [ObservableProperty]
    private List<string> _tagIds = new();

    [ObservableProperty]
    private System.Collections.ObjectModel.ObservableCollection<TaskStep> _steps = new();

    [ObservableProperty]
    private bool _completed;

    [ObservableProperty]
    private CloseRecord? _closeRecord;

    [ObservableProperty]
    private long _createdAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [ObservableProperty]
    private long _modifiedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Inline title editing state (not persisted)</summary>
    [ObservableProperty]
    [BsonIgnore]
    private bool _isEditingTitle;

    [ObservableProperty]
    [BsonIgnore]
    private string _editTitle = string.Empty;

    /// <summary>Number of attachments (ADR-013). Not persisted: attachments live in a
    /// separate local-only collection, so this is refreshed from the DB on load / change.</summary>
    [ObservableProperty]
    [BsonIgnore]
    private int _attachmentCount;

    /// <summary>Local-only attachments (ADR-013), loaded from the separate DB collection
    /// when the task is selected. Never serialized — sync's whole-entity LWW overwrite
    /// must not touch it.</summary>
    [ObservableProperty]
    [BsonIgnore]
    private System.Collections.ObjectModel.ObservableCollection<TaskAttachment> _attachments = new();

    /// <summary>
    /// Convenience: is the task closed?
    /// </summary>
    public bool IsClosed => CloseRecord != null;

    /// <summary>
    /// Convenience: close mode string for display
    /// </summary>
    public string CloseModeDisplay =>
        CloseRecord == null ? "" :
        CloseRecord.CloseMode == CloseMode.Complete ? "Completed" : "Cancelled";

    /// <summary>
    /// Number of completed steps
    /// </summary>
    public int CompletedStepCount => Steps.Count(s => s.Completed);

    /// <summary>
    /// Notify the UI that the completed-step count may have changed
    /// </summary>
    public void NotifyCompletedStepCount() => OnPropertyChanged(nameof(CompletedStepCount));

    /// <summary>
    /// Notify the UI that TagIds changed (a plain List, so it can't self-notify)
    /// </summary>
    public void NotifyTagsChanged() => OnPropertyChanged(nameof(TagIds));

    /// <summary>
    /// Notify the UI that the close-state derived properties may have changed
    /// </summary>
    public void NotifyCloseDisplay()
    {
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(CloseModeDisplay));
    }

    /// <summary>
    /// Deep copy for undo-delete: a fresh instance with new collection / reference
    /// members (TagIds, Steps, CloseRecord), so the restored row never shares mutable
    /// state with the original. Id / ListId / GroupId / Order / timestamps are preserved —
    /// undo re-inserts the same id, which collapses the sync-outbox delete tombstone into
    /// the re-insert (single-slot upsert). UI-only [BsonIgnore] fields are not copied:
    /// attachment count is recomputed on load, attachments reload on selection.
    /// </summary>
    public TaskItem Clone()
    {
        return new TaskItem
        {
            Id = Id,
            Title = Title,
            Note = Note,
            ListId = ListId,
            GroupId = GroupId,
            Order = Order,
            IsImportant = IsImportant,
            IsMyDay = IsMyDay,
            MyDayOrder = MyDayOrder,
            DueDate = DueDate,
            Reminder = Reminder,
            FiredReminder = FiredReminder,
            Recurrence = Recurrence,
            RecurrenceInterval = RecurrenceInterval,
            RecurrenceSeriesId = RecurrenceSeriesId,
            TagIds = new List<string>(TagIds),
            Steps = new System.Collections.ObjectModel.ObservableCollection<TaskStep>(
                Steps.Select(s => new TaskStep
                {
                    Id = s.Id, Title = s.Title, Completed = s.Completed, Order = s.Order,
                })),
            Completed = Completed,
            CloseRecord = CloseRecord == null ? null
                : new CloseRecord { ClosedAt = CloseRecord.ClosedAt, CloseMode = CloseRecord.CloseMode },
            CreatedAt = CreatedAt,
            ModifiedAt = ModifiedAt,
        };
    }
}
