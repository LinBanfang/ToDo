using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ToDo.Converters;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo.Views.Dialogs;

public partial class TagManageDialog : Window
{
    private MainViewModel ViewModel => App.ViewModel!;
    private string _newTagColor = "#0078D4";
    private string[] _tagColors = new[]
    {
        "#E83E8C", "#DC3545", "#FD7E14", "#FFC107", "#FFD700",
        "#28A745", "#20C997", "#17A2B8", "#0DCAF0", "#0D6EFD",
        "#6F42C1", "#7952B3", "#D63384", "#E74C3C", "#E67E22",
        "#2ECC71", "#1ABC9C", "#3498DB", "#9B59B6", "#34495E"
    };

    public TagManageDialog()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarService.Apply(this);
        TagListControl.ItemsSource = ViewModel.Tags;
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Tag tag)
        {
            ShowColorPicker(btn, color =>
            {
                tag.Color = color;
                ViewModel.UpdateTagCommand.Execute(tag);
            });
        }
    }

    private void NewColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            ShowColorPicker(btn, color =>
            {
                _newTagColor = color;
                btn.Background = new SolidColorBrush(ColorParser.ParseColor(color));
            });
        }
    }

    private void ShowColorPicker(Button anchor, Action<string> onColorSelected)
    {
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
        };

        var panel = new StackPanel
        {
            Background = (Brush)Application.Current.FindResource("CardBackgroundBrush"),
        };

        // Fixed palette
        var wrapPanel = new WrapPanel { Width = 200, Margin = new Thickness(3, 3, 3, 0) };
        foreach (var color in _tagColors)
        {
            var colorBtn = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(3),
                Background = new SolidColorBrush(ColorParser.ParseColor(color)),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            colorBtn.Click += (s, _) =>
            {
                onColorSelected(color);
                popup.IsOpen = false;
            };
            wrapPanel.Children.Add(colorBtn);
        }
        panel.Children.Add(wrapPanel);

        // Custom color row: hex input + apply + full picker
        var currentColor = anchor.Tag is Tag t ? t.Color : _newTagColor;
        var row = new Grid { Margin = new Thickness(3, 6, 3, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var hexBox = new TextBox
        {
            Text = currentColor,
            Style = (Style)FindResource("FluentTextBox"),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Padding = new Thickness(6, 2, 6, 2),
        };
        Grid.SetColumn(hexBox, 0);
        row.Children.Add(hexBox);

        var applyBtn = new Button
        {
            Content = Loc.Apply,
            Style = (Style)FindResource("FluentButton"),
            FontSize = 12,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0),
        };
        applyBtn.Click += (s, _) =>
        {
            try
            {
                var hex = NormalizeHex(hexBox.Text);
                ColorParser.ParseColor(hex); // validate before applying
                onColorSelected(hex);
                popup.IsOpen = false;
            }
            catch
            {
                // ignore invalid hex input
            }
        };
        Grid.SetColumn(applyBtn, 1);
        row.Children.Add(applyBtn);

        var moreBtn = new Button
        {
            Content = Loc.MoreColors,
            Style = (Style)FindResource("FluentButton"),
            FontSize = 12,
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(4, 0, 0, 0),
        };
        moreBtn.Click += (s, _) =>
        {
            popup.IsOpen = false;
            ShowCustomColorDialog(currentColor, onColorSelected);
        };
        Grid.SetColumn(moreBtn, 2);
        row.Children.Add(moreBtn);

        panel.Children.Add(row);
        popup.Child = panel;
        popup.IsOpen = true;
    }

    /// <summary>Opens the native color dialog (supports defining custom colors).</summary>
    private void ShowCustomColorDialog(string currentHex, Action<string> onColorSelected)
    {
        var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        try
        {
            dialog.Color = System.Drawing.ColorTranslator.FromHtml(currentHex);
        }
        catch
        {
            dialog.Color = System.Drawing.Color.FromArgb(0, 120, 212);
        }

        var owner = new Win32Window(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK)
        {
            var hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
            onColorSelected(hex);
        }
    }

    /// <summary>Normalizes a user-typed color to "#RRGGBB" (drops an "#AARRGGBB" alpha).</summary>
    private static string NormalizeHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length == 8) hex = hex.Substring(2);
        return "#" + hex;
    }

    /// <summary>Wraps a window handle so the native dialog can be owned by this WPF window.</summary>
    private sealed class Win32Window : System.Windows.Forms.IWin32Window
    {
        public Win32Window(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    private string _tagRenameOriginal = "";

    /// <summary>Snapshots the tag name when editing starts. The rename box binds Name with
    /// UpdateSourceTrigger=LostFocus, and the binding writes the source BEFORE the LostFocus
    /// handler runs — so TagName_LostFocus must compare against this captured value, not
    /// tag.Name (which is already overwritten by then). Mirrors DetailField_GotFocus.</summary>
    private void TagName_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            _tagRenameOriginal = tb.Text;
    }

    /// <summary>Persists an inline rename. The Name binding only mutates the in-memory
    /// object, so without this the edit is lost on reload — and a rename that collides
    /// with another tag would hit the unique index and crash. Reject duplicates here
    /// (with a message) and push every other change through UpdateTag. Mirrors the
    /// detail pane's DetailField_LostFocus: force the pending binding update first,
    /// then compare against the captured original to detect a real change.</summary>
    private void TagName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || box.Tag is not Tag tag) return;
        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource(); // reach tag.Name regardless of handler/binding order
        var name = box.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            box.Text = tag.Name; // reject empty names
            return;
        }
        if (ViewModel.Tags.Any(t => t.Id != tag.Id && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            box.Text = tag.Name; // revert to the persisted name
            FluentDialog.Show(this, Loc.TagNameExists(name), Loc.ManageTags, MsgKind.Warning);
            return;
        }
        if (name != _tagRenameOriginal)
        {
            tag.Name = name;
            ViewModel.UpdateTagCommand.Execute(tag);
        }
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = NewTagNameBox.Text.Trim();
        if (string.IsNullOrEmpty(name)) return;

        if (ViewModel.Tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            FluentDialog.Show(this, Loc.TagNameExists(name), Loc.ManageTags, MsgKind.Warning);
            return;
        }

        ViewModel.CreateTagCommand.Execute((name, _newTagColor));
        NewTagNameBox.Text = "";
        _newTagColor = "#0078D4";
        NewTagColorBtn.Background = new SolidColorBrush(ColorParser.ParseColor("#0078D4"));
    }

    private void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Tag tag)
        {
            if (FluentDialog.Confirm(this, Loc.ConfirmDeleteMsg(tag.Name), Loc.ConfirmDelete))
                ViewModel.DeleteTagCommand.Execute(tag);
        }
    }
}
