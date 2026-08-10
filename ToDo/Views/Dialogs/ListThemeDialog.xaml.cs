using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ToDo.Converters;
using ToDo.Models;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo.Views.Dialogs;

/// <summary>Lets the user pick a per-list background (none / solid color / local image).
/// The solid color syncs like any list field; image bytes stay on this device only.</summary>
public partial class ListThemeDialog : Window
{
    private readonly TaskList _list;
    private MainViewModel ViewModel => App.ViewModel!;
    private DatabaseService Db => App.Database!;

    private ListBackgroundType _type;
    private string _color;
    private byte[]? _bytes;
    private string? _fileName;
    private bool _dirty;
    private int _opacity;
    private bool _opacityDirty;

    private string[] _bgColors = new[]
    {
        "#E83E8C", "#DC3545", "#FD7E14", "#FFC107", "#FFD700",
        "#28A745", "#20C997", "#17A2B8", "#0DCAF0", "#0D6EFD",
        "#6F42C1", "#7952B3", "#D63384", "#E74C3C", "#E67E22",
        "#2ECC71", "#1ABC9C", "#3498DB", "#9B59B6", "#34495E"
    };

    public ListThemeDialog(TaskList list)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarService.Apply(this);

        _list = list;
        _type = list.BackgroundType;
        _color = list.BackgroundColor;
        if (_type == ListBackgroundType.Image)
        {
            _bytes = Db.GetListBackgroundData(list.Id);
            _fileName = Db.GetListBackgroundFileName(list.Id);
        }
        _opacity = Math.Clamp(Db.GetListBackgroundOpacity(list.Id), 20, 100);

        PreviewTitle.Text = list.DisplayName;
        OpacitySlider.Value = _opacity;   // fires ValueChanged → label + live preview
        OpacityValue.Text = _opacity + "%";
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var windowBg = (Brush)Application.Current.FindResource("AppBackgroundBrush");
        var opacity = OpacitySlider.Value / 100.0;

        Brush? bg = null;
        switch (_type)
        {
            case ListBackgroundType.Solid:
                bg = new SolidColorBrush(ColorParser.ParseColor(_color)) { Opacity = opacity };
                bg.Freeze();
                break;
            case ListBackgroundType.Image when _bytes is { Length: > 0 }:
                bg = BuildImageBrush(_bytes!, opacity);
                break;
        }

        PreviewBox.Background = bg ?? windowBg;
        PreviewMask.Visibility = _type == ListBackgroundType.Image ? Visibility.Visible : Visibility.Collapsed;

        var swatchColor = _type == ListBackgroundType.Solid
            ? (Brush)new SolidColorBrush(ColorParser.ParseColor(_color))
            : (Brush)Application.Current.FindResource("TextSecondaryBrush");
        ColorSwatchBtn.Background = swatchColor;
    }

    private static ImageBrush BuildImageBrush(byte[] data, double opacity)
    {
        using var stream = new MemoryStream(data);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        var brush = new ImageBrush(bitmap)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            Opacity = opacity,
        };
        brush.Freeze();
        return brush;
    }

    private void NoBackground_Click(object sender, RoutedEventArgs e)
    {
        _type = ListBackgroundType.None;
        _color = "";
        _bytes = null;
        _fileName = null;
        _dirty = true;
        UpdatePreview();
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        ShowColorPicker(btn, color =>
        {
            _type = ListBackgroundType.Solid;
            _color = color;
            _bytes = null;
            _fileName = null;
            _dirty = true;
            UpdatePreview();
        });
    }

    private void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = Loc.ImageFileFilter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true) return;

        var file = new FileInfo(dialog.FileName);
        if (file.Length > 8 * 1024 * 1024)
        {
            FluentDialog.Show(this, Loc.ImageTooLarge(8), Loc.ListTheme, MsgKind.Warning);
            return;
        }

        _type = ListBackgroundType.Image;
        _bytes = File.ReadAllBytes(dialog.FileName);
        _fileName = Path.GetFileName(dialog.FileName);
        _dirty = true;
        UpdatePreview();
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        if (_bytes == null) return;
        _bytes = null;
        _fileName = null;
        _type = string.IsNullOrEmpty(_color) ? ListBackgroundType.None : ListBackgroundType.Solid;
        _dirty = true;
        UpdatePreview();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        OpacityValue.Text = (int)Math.Round(OpacitySlider.Value) + "%";
        UpdatePreview();   // live preview, so strength is judged against the real backdrop
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_type == ListBackgroundType.Image && _bytes == null)
            _type = ListBackgroundType.None;

        var opacity = (int)Math.Round(OpacitySlider.Value);
        if (opacity != _opacity) _opacityDirty = true;
        if (_dirty || _opacityDirty)
            ViewModel.SetListTheme(_list, _type, _color, _bytes, _fileName, opacity);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ---- Color picker (copied from TagManageDialog) ----

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

        var wrapPanel = new WrapPanel { Width = 200, Margin = new Thickness(3, 3, 3, 0) };
        foreach (var color in _bgColors)
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

        var currentColor = string.IsNullOrEmpty(_color) ? "#0078D4" : _color;
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
}
