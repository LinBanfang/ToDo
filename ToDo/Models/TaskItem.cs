using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;

namespace ToDo.Models;

public partial class TaskItem : ObservableObject
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
}
