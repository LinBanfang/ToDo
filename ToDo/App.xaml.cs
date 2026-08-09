using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using ToDo.Services;
using ToDo.Sync;
using ToDo.ViewModels;
using ToDo.Views.Dialogs;

namespace ToDo;

public partial class App : Application
{
    public static DatabaseService? Database { get; private set; }
    public static MainViewModel? ViewModel { get; private set; }
    public static ReminderService? Reminders { get; private set; }
    public static SyncService? Sync { get; private set; }
    public static TrayService? Tray { get; private set; }

    /// <summary>Held for the whole process so the OS keeps the single-instance lock.</summary>
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Only one process may touch the LiteDB file at a time (Connection=direct opens
        // it with FileShare.ReadWrite, so a second instance would race writes and risk
        // "database locked" / corruption). The mutex is checked before anything else.
        if (!AcquireSingleInstance())
        {
            // Another instance is already running; restore its window and exit quietly.
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        // Register crash handlers before anything that can throw, so an unhandled
        // exception is logged and (with a visible window) surfaced instead of silently
        // killing the process and losing the tray session.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticLog.Error("unhandled", $"AppDomain exception: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticLog.Error("unhandled", $"Unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

        // Swap in a restored backup before the database is opened
        RestorePendingDatabase();

        Database = new DatabaseService(ResolveDbPath());

        // The sync engine (in ToDo.Core) reports through the same app log as the rest of
        // the app; the seam is a no-op until wired here (ADR-009).
        SyncDiagnostics.Log = m => DiagnosticLog.Info("sync", m);
        SyncDiagnostics.LogWarn = m => DiagnosticLog.Warn("sync", m);
        SyncDiagnostics.LogError = m => DiagnosticLog.Error("sync", m);

        // Sync is created before the ViewModel so the settings SyncSection can subscribe
        // to its StatusChanged; the refresh hook + timer start once the VM exists.
        Sync = new SyncService(Database, Dispatcher);
        ViewModel = new MainViewModel(Database);
        Tray = new TrayService();
        Reminders = new ReminderService(Database);
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

    /// <summary>Attempts to take the single-instance mutex. Returns false when another
    /// process already owns it, meaning this instance must not touch the database.</summary>
    private static bool AcquireSingleInstance()
    {
        try
        {
            // "Local\" scopes the lock to the logon session (the DB lives in this user's
            // %LOCALAPPDATA%, so another user's session would use a different file anyway).
            _singleInstanceMutex = new Mutex(initiallyOwned: false, @"Local\ToDo.SingleInstance");
            bool acquired;
            try
            {
                acquired = _singleInstanceMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                // The previous holder crashed without releasing; take the lock over so a
                // restart isn't blocked forever by a mutex no process owns anymore.
                acquired = true;
            }
            return acquired;
        }
        catch
        {
            // If the mutex can't even be created, don't let it block startup.
            return true;
        }
    }

    /// <summary>Best-effort bring-forward of the running instance's window, so a second
    /// launch visibly lands on the existing app instead of being a silent no-op. The
    /// mutex alone already prevents a second process; this is only for user feedback.</summary>
    private static void ActivateExistingInstance()
    {
        try
        {
            var self = Process.GetCurrentProcess().Id;
            var exeName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "ToDo.exe");
            foreach (var p in Process.GetProcessesByName(exeName))
            {
                if (p.Id == self) continue;
                var hwnd = p.MainWindowHandle;
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);      // unhide a tray-minimized window
                    SetForegroundWindow(hwnd);
                }
                break;
            }
        }
        catch
        {
            // Best effort only — never let window-restore block startup.
        }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// UI-thread safety net: log, then keep the app running with an error dialog when a
    /// window exists to tell the user about it (exiting here would discard the tray
    /// session and any unsaved edits). During a broken startup with no window, rethrow
    /// instead so the failure stays loud instead of running a headless process.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticLog.Error("unhandled", $"Dispatcher exception: {e.Exception}");
        if (Current.MainWindow is { IsLoaded: true } win)
        {
            try
            {
                FluentDialog.Show(win, e.Exception.Message, Loc.Error, MsgKind.Error);
            }
            catch
            {
                // The dialog itself failed; the log line above is all we have.
            }
            e.Handled = true;
        }
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
