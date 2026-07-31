using System.Windows;
using Microsoft.Win32;

namespace ToDo.Views.Dialogs;

public partial class DbPathDialog : Window
{
    public string ResultPath { get; private set; } = "";

    public DbPathDialog(string currentPath)
    {
        InitializeComponent();
        PathBox.Text = currentPath;
        PathBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Select database file",
            FileName = "todo.db",
            Filter = "Database files (*.db)|*.db|All files (*.*)|*.*",
            DefaultExt = "db"
        };
        if (dialog.ShowDialog() == true)
        {
            PathBox.Text = dialog.FileName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim();
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show("Please enter a valid path.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ResultPath = path;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
