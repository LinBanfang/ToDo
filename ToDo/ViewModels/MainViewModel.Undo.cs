using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ToDo.ViewModels;

public partial class MainViewModel
{
    // ─── Undo (single-slot toast undo for 完成 / 删除) ─────────
    // The VM exposes only the slot + command + clear; the 5s auto-dismiss DispatcherTimer
    // lives in MainWindow code-behind, so the VM stays unit-testable (no UI timer
    // dependency in command tests). A new operation replaces whatever is pending.
    [ObservableProperty]
    private UndoEntry? _currentUndo;

    [RelayCommand]
    private void Undo()
    {
        var entry = CurrentUndo;
        ClearUndo();          // clear first: if the action pushes a new undo it replaces this slot
        entry?.Action();
    }

    /// <summary>Replace whatever undo is pending with this one (single slot).</summary>
    public void PushUndo(string message, Action action) => CurrentUndo = new UndoEntry(message, action);

    /// <summary>Called by the view's auto-dismiss timer and by Undo().</summary>
    public void ClearUndo() => CurrentUndo = null;
}

/// <summary>Message + the redo-able action for one undo slot (immutable).</summary>
public sealed class UndoEntry
{
    public string Message { get; }
    public Action Action { get; }

    public UndoEntry(string message, Action action)
    {
        Message = message;
        Action = action;
    }
}
