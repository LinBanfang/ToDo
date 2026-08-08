using System.IO;
using System.Windows;
using ToDo.Services;
using ToDo.ViewModels;

namespace ToDo;

public partial class App : Application
{
    public static DatabaseService? Database { get; private set; }
    public static MainViewModel? ViewModel { get; private set; }
    public static ReminderService? Reminders { get; private set; }
    public static SyncService? Sync { get; private set; }
    public static TrayService? Tray { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Swap in a restored backup before the database is opened
        RestorePendingDatabase();

        Database = new DatabaseService(ResolveDbPath());

        // Sync is created before the ViewModel so the settings SyncSection can subscribe
        // to its StatusChanged; the refresh hook + timer start once the VM exists.
        Sync = new SyncService(Database, Dispatcher);
        ViewModel = new MainViewModel(Database);
        // Tray comes before Reminders so the reminder service shares the same icon.
        Tray = new TrayService();
        Reminders = new ReminderService(Database, Tray.Icon);
        Sync.SetRefreshAction(() => { ViewModel.LoadAll(); ViewModel.RefreshActiveTasks(); });

        // With ShutdownMode=OnExplicitShutdown the app keeps running in the tray until
        // told to exit; a Windows logout/shutdown must still terminate it cleanly.
        SessionEnding += (s, se) =>
        {
            if (!WindowManager.IsQuitting) WindowManager.Quit();
        };

        // Apply the persisted language + theme before the first window loads
        Loc.SetLanguage(SettingsService.Current.Language == "English"
            ? AppLanguage.English : AppLanguage.Chinese);
        ThemeService.Apply(ViewModel.Theme);

        var mainWindow = new MainWindow { DataContext = ViewModel };
        mainWindow.Icon = new System.Windows.Media.Imaging.BitmapImage(
            new Uri("pack://application:,,,/Resources/app.ico"));
        WindowManager.Init(mainWindow);
        mainWindow.Show();

        Sync.Start();

        UpdateService.Configure();
        if (SettingsService.Current.CheckForUpdatesOnStartup)
        {
            Dispatcher.BeginInvoke(() => UpdateService.CheckForUpdates(),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>
    /// Resolves the configured DB path, migrating a legacy DB that sat next to the
    /// exe (pre-1.0 layout) to the configured location on first run after upgrade.
    /// Moved here when DatabaseService was extracted to ToDo.Core so the library
    /// stays decoupled from SettingsService.
    /// </summary>
    private static string ResolveDbPath()
    {
        SettingsService.Load();
        var configured = SettingsService.Current.DbPath;
        var defaultPath = SettingsService.DefaultDbPath;

        // Check for old DB at exe location and migrate
        var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "todo.db");
        if (File.Exists(legacyPath) && !File.Exists(configured))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configured)!);
            File.Copy(legacyPath, configured);
        }

        return configured;
    }

    /// <summary>Applies a pending "restore from backup" staged by the settings page.</summary>
    private static void RestorePendingDatabase()
    {
        var pending = SettingsService.Current.PendingRestorePath;
        if (string.IsNullOrEmpty(pending) || !File.Exists(pending)) return;

        var dbPath = SettingsService.Current.DbPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        File.Copy(pending, dbPath, overwrite: true);
        File.Delete(pending);

        SettingsService.Current.PendingRestorePath = null;
        SettingsService.Save();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Sync?.Dispose();
        Reminders?.Dispose();
        Tray?.Dispose();
        Database?.Dispose();
        base.OnExit(e);
    }
}
