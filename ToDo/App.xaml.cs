using System.Windows;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo;

public partial class App : Application
{
    public static DatabaseService? Database { get; private set; }
    public static MainViewModel? ViewModel { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Database = new DatabaseService();
        ViewModel = new MainViewModel(Database);

        var mainWindow = new MainWindow { DataContext = ViewModel };
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Database?.Dispose();
        base.OnExit(e);
    }
}
