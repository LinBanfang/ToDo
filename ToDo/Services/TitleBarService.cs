using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ToDo.Services;

/// <summary>
/// Colors the OS-drawn window title bar via DWM so it follows the app's
/// Light/Dark theme instead of the Windows system theme. Immersive dark mode
/// works on Win10 1809+; on Win11 (22000+) we additionally pin the caption,
/// text and border colors to the active theme palette for an exact match.
/// </summary>
public static class TitleBarService
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20; // Win10 1809+, Win11
    private const int DWMWA_BORDER_COLOR = 34;            // Win11 22000+
    private const int DWMWA_CAPTION_COLOR = 35;           // Win11 22000+
    private const int DWMWA_TEXT_COLOR = 36;              // Win11 22000+

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Re-applies the current theme to every open window (called after a theme swap).</summary>
    public static void ApplyAll()
    {
        foreach (Window w in Application.Current.Windows)
            Apply(w);
    }

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (hwnd == IntPtr.Zero) return;

        bool isDark = SettingsService.Current.Theme == "Dark";
        int dark = isDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Pull the palette from the live theme dictionary so the caption color
            // always tracks the real brushes, not a hardcoded copy.
            int bg = ToColorRef(TryGetColor("AppBackgroundBrush") ??
                (isDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF2, 0xF1)));
            int fg = ToColorRef(TryGetColor("TextPrimaryBrush") ??
                (isDark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x20, 0x1F, 0x1E)));
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref bg, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref fg, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref bg, sizeof(int));
        }
    }

    private static Color? TryGetColor(string key) =>
        Application.Current.TryFindResource(key) is SolidColorBrush b ? b.Color : null;

    /// <summary>WPF Color → COLORREF (0x00BBGGRR) used by DwmSetWindowAttribute.</summary>
    private static int ToColorRef(Color c) => c.B << 16 | c.G << 8 | c.R;
}
