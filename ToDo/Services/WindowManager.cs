using System.Windows;

namespace ToDo.Services;

/// <summary>
/// Coordinates the window mode switch between the main window, the sticky-note
/// mini window and the tray. One window is visible at a time: OpenSticky hides the
/// main window, ShowMain hides the sticky. The tray "退出" is the app's only real
/// exit; closing the last window otherwise keeps the process alive (tray mode).
/// </summary>
public static class WindowManager
{
    private static MainWindow? _main;
    private static StickyWindow? _sticky;

    /// <summary>True while the app is really exiting — windows let themselves close.</summary>
    public static bool IsQuitting { get; private set; }

    /// <summary>True while a language-change rebuild is closing the old main window —
    /// MainWindow.OnClosing lets it close instead of canceling to the tray.</summary>
    public static bool IsRebuilding { get; private set; }

    public static void Init(MainWindow main)
    {
        _main = main;
        if (App.Tray != null)
        {
            App.Tray.OpenMainRequested += ShowMain;
            App.Tray.OpenStickyRequested += OpenSticky;
            App.Tray.ExitRequested += Quit;
        }
    }

    /// <summary>Show the main window (tray double-click / menu "打开主界面" /
    /// sticky "返回主界面" button).</summary>
    public static void ShowMain()
    {
        if (_sticky != null) _sticky.Close();
        if (_main == null) return;
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        _main.Show();
        _main.Activate();
    }

    /// <summary>Show the sticky note and hide the main window (single instance).</summary>
    public static void OpenSticky()
    {
        if (_sticky is { IsLoaded: true })
        {
            _sticky.Activate();
            return;
        }
        _main?.Hide();
        _sticky = new StickyWindow();
        _sticky.Closed += OnStickyClosed;
        _sticky.Show();
    }

    /// <summary>Fired when the sticky note closes (user X or programmatic). Closing
    /// always returns to the tray — the main window only comes back via ShowMain().</summary>
    private static void OnStickyClosed(object? sender, EventArgs e)
    {
        if (sender is StickyWindow sw) sw.Closed -= OnStickyClosed;
        _sticky = null;
    }

    /// <summary>The window currently on screen (sticky if open, else main), or null —
    /// a WPF ContextMenu placement source while in tray mode (must not Show anything).</summary>
    public static Window? CurrentWindow()
    {
        if (_sticky is { IsLoaded: true }) return _sticky;
        return _main;
    }

    /// <summary>Finds a visible window to own dialogs; shows the main window if hidden
    /// (needed while in tray/sticky mode, e.g. for the update dialog).</summary>
    public static Window? ResolveDialogOwner()
    {
        if (_sticky is { IsLoaded: true }) return _sticky;
        if (_main != null)
        {
            if (!_main.IsVisible) _main.Show();
            return _main;
        }
        return Application.Current?.MainWindow;
    }

    /// <summary>
    /// Swaps in a fresh main window after a language change. XAML {x:Static Loc.*}
    /// bindings are resolved when the window loads, so the long-lived windows must be
    /// recreated to pick up the new language. The App.ViewModel singleton (and with it
    /// all user state, including IsSettingsMode) survives; window geometry is preserved.
    /// </summary>
    public static void RebuildForLanguageChange()
    {
        if (IsQuitting) return;

        // Tooltip + context-menu strings resolve Loc at construction — rebuild them.
        App.Tray?.Refresh();

        var main = _main;
        var left = main?.Left ?? 0;
        var top = main?.Top ?? 0;
        var width = main?.Width ?? 1200;
        var height = main?.Height ?? 800;
        var state = main?.WindowState ?? WindowState.Normal;
        var wasVisible = main?.IsVisible ?? true;

        // The sticky note reopens in the new language (it is recreated per open);
        // not reachable while the settings page is up, but close defensively.
        _sticky?.Close();
        _sticky = null;

        // Bypass MainWindow.OnClosing, which otherwise cancels to the tray.
        IsRebuilding = true;
        try { main?.Close(); }
        finally { IsRebuilding = false; }

        var next = App.CreateMainWindow();
        next.Left = left;
        next.Top = top;
        next.Width = width;
        next.Height = height;
        next.WindowState = state;
        _main = next;
        Application.Current.MainWindow = next;
        if (wasVisible) next.Show();
    }

    /// <summary>The only real exit: tray menu "退出".</summary>
    public static void Quit()
    {
        IsQuitting = true;
        Application.Current.Shutdown();
    }
}
