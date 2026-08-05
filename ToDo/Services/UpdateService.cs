using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using AutoUpdaterDotNET;

namespace ToDo.Services;

/// <summary>
/// Wires the vendored AutoUpdater.NET into the app: checks GitHub Releases for the
/// latest tag and shows a WPF dialog when a newer version is available.
/// </summary>
public static class UpdateService
{
    private const string Repo = "LinBanfang/ToDo";
    private static string? _latestReleaseBody;

    public static void Configure()
    {
        AutoUpdater.InstalledVersion = typeof(UpdateService).Assembly.GetName().Version;
        AutoUpdater.PersistenceProvider = new JsonFilePersistenceProvider(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToDo", "updater.json"));
        AutoUpdater.ParseUpdateInfoEvent += ParseUpdateInfo;
        AutoUpdater.CheckForUpdateEvent += args =>
            Application.Current?.Dispatcher.Invoke(() => OnUpdateChecked(args));
    }

    public static void CheckForUpdates() => AutoUpdater.Start();

    private static void ParseUpdateInfo(ParseUpdateInfoEventArgs args)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToDo-Updater");
            var json = client.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest").Result;
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString();
            _latestReleaseBody = root.TryGetProperty("body", out var body) ? body.GetString() : "";
            var zipUrl = root.GetProperty("assets").EnumerateArray()
                .First(a => a.GetProperty("name").GetString()?.EndsWith(".zip") == true)
                .GetProperty("browser_download_url").GetString();

            args.UpdateInfo = new UpdateInfoEventArgs
            {
                CurrentVersion = tag?.TrimStart('v'),
                DownloadURL = zipUrl,
                ChangelogURL = $"https://github.com/{Repo}/releases/tag/{tag}",
            };
        }
        catch
        {
            args.UpdateInfo = null;
        }
    }

    private static void OnUpdateChecked(UpdateInfoEventArgs args)
    {
        if (args.Error != null || !args.IsUpdateAvailable) return;
        var owner = Application.Current?.MainWindow;
        if (owner == null) return;
        var dialog = new Views.Dialogs.UpdateDialog(args, _latestReleaseBody) { Owner = owner };
        dialog.ShowDialog();
    }
}
