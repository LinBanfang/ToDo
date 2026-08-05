using System.Windows;
using System.Windows.Controls;

namespace ToDo.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private void ManageTags_Click(object sender, RoutedEventArgs e)
    {
        var owner = Application.Current.MainWindow;
        var dialog = new Dialogs.TagManageDialog { Owner = owner };
        dialog.ShowDialog();
    }
}
