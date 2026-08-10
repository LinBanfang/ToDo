using CommunityToolkit.Mvvm.ComponentModel;

namespace ToDo.Models;

/// <summary>
/// A list's background image bytes, keyed by list id (ADR-014). Local-only: never
/// synced, so it lives in its own untracked collection — the sync layer's whole-entity
/// LWW overwrite of TaskList never touches it. Bytes are stored inside the LiteDB file
/// to keep the single-file data model intact (backup / restore / db-path migration all
/// copy one file).
/// </summary>
public partial class ListBackground : ObservableObject
{
    /// <summary>Equals the list id so Upsert keeps exactly one row per list.</summary>
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _listId = string.Empty;

    /// <summary>Original file name (displayed in the theme dialog).</summary>
    [ObservableProperty]
    private string _fileName = string.Empty;

    /// <summary>Image bytes, embedded in the database.</summary>
    [ObservableProperty]
    private byte[] _data = Array.Empty<byte>();
}
