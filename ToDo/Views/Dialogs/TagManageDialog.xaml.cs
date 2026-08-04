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

        var wrapPanel = new WrapPanel
        {
            Width = 200,
            Background = (Brush)Application.Current.FindResource("CardBackgroundBrush"),
        };

        foreach (var color in _tagColors)
        {
            var colorBtn = new Button
            {
                Width = 28,
                Height = 28,
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

        popup.Child = wrapPanel;
        popup.IsOpen = true;
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var name = NewTagNameBox.Text.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            ViewModel.CreateTagCommand.Execute((name, _newTagColor));
            NewTagNameBox.Text = "";
            _newTagColor = "#0078D4";
            NewTagColorBtn.Background = new SolidColorBrush(ColorParser.ParseColor("#0078D4"));
        }
    }

    private void DeleteTag_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Tag tag)
        {
            if (MessageBox.Show(Loc.ConfirmDeleteMsg(tag.Name), Loc.ConfirmDelete,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                ViewModel.DeleteTagCommand.Execute(tag);
        }
    }
}
