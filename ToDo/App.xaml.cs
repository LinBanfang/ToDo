using System.Windows;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo;

public partial class App : Application
{
    public static DatabaseService? Database { get; private set; }
    public static MainViewModel? ViewModel { get; private set; }
    public static ReminderService? Reminders { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Database = new DatabaseService();
        ViewModel = new MainViewModel(Database);
        Reminders = new ReminderService(Database);

        // Apply the persisted theme before the first window loads
        ThemeService.Apply(ViewModel.Theme);

        var mainWindow = new MainWindow { DataContext = ViewModel };
        mainWindow.Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Resources/app.ico"));
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Reminders?.Dispose();
        Database?.Dispose();
        base.OnExit(e);
    }
}
