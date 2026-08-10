using System.Windows;
using System.Windows.Media;
using ToDo.Services;

namespace ToDo.Views.Dialogs;

/// <summary>Kind of message dialog; drives the icon shown.</summary>
public enum MsgKind
{
    Info,
    Warning,
    Error,
    Delete,
}

/// <summary>
/// Fluent-styled message / confirmation dialog replacing the native MessageBox
/// so every modal in the app shares the same visual language.
/// </summary>
public partial class FluentDialog : Window
{
    // Segoe MDL2 Assets glyphs
    private const string IconInfo = "";
    private const string IconWarning = "";
    private const string IconError = "";
    private const string IconDelete = "";

    public FluentDialog(string message, string title, MsgKind kind = MsgKind.Info)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarService.Apply(this);
        Title = title;
        MessageText.Text = message;

        switch (kind)
        {
            case MsgKind.Warning:
                // Orange rather than the raw yellow so it stays legible on the light card.
                Configure(IconWarning, (Brush)FindResource("AccentOrange"), confirm: false);
                break;
            case MsgKind.Error:
                Configure(IconError, (Brush)FindResource("AccentRedBrush"), confirm: false);
                break;
            case MsgKind.Delete:
                Configure(IconDelete, (Brush)FindResource("AccentRedBrush"), confirm: true);
                break;
            default:
                Configure(IconInfo, (Brush)FindResource("TextAccentBrush"), confirm: false);
                break;
        }
    }

    private void Configure(string glyph, Brush color, bool confirm)
    {
        IconText.Text = glyph;
        IconText.Foreground = color;
        CancelButton.Visibility = confirm ? Visibility.Visible : Visibility.Collapsed;
        if (confirm)
        {
            DeleteButton.Visibility = Visibility.Visible;
            DeleteButton.Content = Loc.Delete;
        }
        else
        {
            OkButton.Visibility = Visibility.Visible;
            OkButton.Content = Loc.OK;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>Shows a single-button info / warning / error box.</summary>
    public static void Show(Window? owner, string message, string title, MsgKind kind = MsgKind.Info)
    {
        var dialog = new FluentDialog(message, title, kind) { Owner = owner };
        dialog.ShowDialog();
    }

    /// <summary>Shows a confirm box; returns true when the destructive action is confirmed.</summary>
    public static bool Confirm(Window? owner, string message, string title, MsgKind kind = MsgKind.Delete)
    {
        var dialog = new FluentDialog(message, title, kind) { Owner = owner };
        return dialog.ShowDialog() == true;
    }
}
