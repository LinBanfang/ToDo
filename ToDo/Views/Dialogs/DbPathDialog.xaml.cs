using System.Windows;
using Microsoft.Win32;
using ToDo.Services;

namespace ToDo.Views.Dialogs;

public partial class DbPathDialog : Window
{
    public string ResultPath { get; private set; } = "";

    public DbPathDialog(string currentPath)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarService.Apply(this);
        PathBox.Text = currentPath;
        PathBox.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = Loc.SelectDbFile,
            FileName = "todo.db",
            Filter = Loc.DbFileFilter,
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
            FluentDialog.Show(this, Loc.InvalidPathMsg, Loc.Error, MsgKind.Warning);
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
