using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WF = System.Windows.Forms;

namespace ToDo.Services;

/// <summary>
/// Owns the single system-tray NotifyIcon: right-click shows a WPF ContextMenu
/// (open main window / sticky note / exit), double-click restores the main window.
/// The icon is shared with <see cref="ReminderService"/> for reminder balloons;
/// created in App.OnStartup and disposed exactly once in App.OnExit. Using a WPF
/// ContextMenu means it inherits the app's global Fluent menu style — rounded,
/// themed, and following Light/Dark switches automatically.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly ContextMenu _menu;

    public WF.NotifyIcon Icon { get; }

    /// <summary>Double-click the tray icon — restore the main window.</summary>
    public event Action? OpenMainRequested;

    /// <summary>Tray menu "迷你便笺" — switch to the sticky note.</summary>
    public event Action? OpenStickyRequested;

    /// <summary>Tray menu "退出" — the app's only real exit path.</summary>
    public event Action? ExitRequested;

    public TrayService()
    {
        Icon = new WF.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = Loc.AppTitle,
        };

        // The app's global Fluent ContextMenu style applies here automatically —
        // the same rounded, themed look as the in-app right-click menus.
        _menu = new ContextMenu();
        _menu.Items.Add(NewItem(Loc.OpenMainWindow, () => OpenMainRequested?.Invoke()));
        _menu.Items.Add(NewItem(Loc.StickyNote, () => OpenStickyRequested?.Invoke()));
        _menu.Items.Add(new Separator());
        _menu.Items.Add(NewItem(Loc.ExitApp, () => ExitRequested?.Invoke()));

        Icon.MouseUp += (_, e) =>
        {
            if (e.Button == WF.MouseButtons.Right) ShowMenu();
        };

        // WinForms fires MouseClick (twice) before MouseDoubleClick; only the
        // double-click should restore the main window, so ignore single clicks.
        Icon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == WF.MouseButtons.Left) OpenMainRequested?.Invoke();
        };
    }

    private static MenuItem NewItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private void ShowMenu()
    {
        // Defer one pump: opening a WPF popup while the tray's right-click is still
        // being processed makes it close instantly (it "sees" a click outside).
        Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input, new Action(OpenMenu));
    }

    private void OpenMenu()
    {
        // A WPF ContextMenu needs a PresentationSource; the current window (sticky
        // if open, else main — even when hidden in tray mode) provides one.
        _menu.Placement = PlacementMode.MousePoint;
        _menu.PlacementTarget = WindowManager.CurrentWindow() ?? Application.Current?.MainWindow;
        _menu.IsOpen = true;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var exe = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
                return System.Drawing.Icon.ExtractAssociatedIcon(exe) ?? SystemIcons.Application;
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        Icon.Visible = false;
        Icon.Dispose();
    }
}
