namespace ToDo;

/// <summary>
/// Maps the Ctrl+digit system-list shortcut to the fixed system list id. Kept as a
/// standalone pure class so the keyboard→list mapping is unit-testable (MainWindow's
/// OnKeyDown only forwards the pressed digit).
/// </summary>
public static class KeyboardShortcutMap
{
    /// <summary>System list id for a Ctrl+1..4 press (order follows the sidebar:
    /// My Day / Important / Planned / Tasks), or null for any other digit.</summary>
    public static string? SystemListId(int digit) => digit switch
    {
        1 => "list-myday",
        2 => "list-important",
        3 => "list-planned",
        4 => "list-tasks",
        _ => null,
    };
}
