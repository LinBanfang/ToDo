using CommunityToolkit.Mvvm.ComponentModel;

namespace ToDo.Models;

/// <summary>
/// A file attached to a task. Local-only: never synced (ADR-013), so it is NOT part of
/// the TaskItem entity surface — the sync layer's whole-entity LWW overwrite never touches it.
/// Bytes are stored inside the LiteDB file to keep the single-file data model intact
/// (backup / restore / db-path migration all copy one file).
/// </summary>
public partial class TaskAttachment : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _taskId = string.Empty;

    /// <summary>Original file name (displayed and used to pick the opener extension).</summary>
    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private long _size;

    /// <summary>File bytes, embedded in the database.</summary>
    [ObservableProperty]
    private byte[] _data = Array.Empty<byte>();

    [ObservableProperty]
    private long _addedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Human-readable size (e.g. "1.2 MB") for the detail pane list.</summary>
    public string SizeDisplay
    {
        get
        {
            double s = Size;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (s >= 1024 && unit < units.Length - 1) { s /= 1024; unit++; }
            return unit == 0 ? $"{Size} B" : $"{s:0.#} {units[unit]}";
        }
    }

    partial void OnSizeChanged(long value) => OnPropertyChanged(nameof(SizeDisplay));
}
