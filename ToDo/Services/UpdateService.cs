#nullable disable
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using AutoUpdaterDotNET;

namespace ToDo.Services;

/// <summary>One auto-update feed the app checks, in order.</summary>
public sealed class UpdateSource
{
    /// <summary>"github" / "gitee" (JSON releases API) or "appcast" (AutoUpdater.NET XML).</summary>
    public string Type { get; init; } = "";
    public string Url { get; init; } = "";
}

/// <summary>
/// Wires the vendored AutoUpdater.NET into the app. Checks multiple update sources
/// (GitHub, Gitee, or a private appcast server) in order; the first that responds
/// wins. Shows a WPF dialog when a newer version is available.
/// </summary>
public static class UpdateService
{
    // Tried in order; add/edit/remove sources here. For Gitee, mirror the repo first
    // (github.com/LinBanfang/ToDo → gitee.com/yourname/ToDo) and put the tag + zip asset
    // on the release. For a private server, host an AutoUpdater.NET appcast XML with
    // <version>, <url> (absolute zip URL) and <changelog>.
    private static readonly UpdateSource[] Sources =
    {
        new() { Type = "github", Url = "https://api.github.com/repos/LinBanfang/ToDo/releases/latest" },
        new() { Type = "gitee", Url = "https://gitee.com/api/v5/repos/LinBanfang/ToDo/releases/latest" },
        // new() { Type = "appcast", Url = "https://example.com/todo/appcast.xml" },
    };

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
        foreach (var source in Sources)
        {
            try
            {
                if (TryGetLatest(source, out var version, out var downloadUrl, out var body, out var changelogUrl))
                {
                    _latestReleaseBody = body;
                    args.UpdateInfo = new UpdateInfoEventArgs
                    {
                        CurrentVersion = version,
                        DownloadURL = downloadUrl,
                        ChangelogURL = changelogUrl,
                    };
                    return;
                }
            }
            catch
            {
                // try the next source
            }
        }

        args.UpdateInfo = null;
    }

    private static bool TryGetLatest(UpdateSource source, out string version, out string downloadUrl,
        out string body, out string changelogUrl)
    {
        version = downloadUrl = body = changelogUrl = "";

        if (source.Type == "appcast")
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ToDo-Updater");
            var xml = client.GetStringAsync(source.Url).Result;

            UpdateInfoEventArgs info;
            var previousBaseUri = AutoUpdater.BaseUri;
            AutoUpdater.BaseUri = new Uri(source.Url); // resolve relative <url> against the appcast
            try
            {
                using var reader = XmlReader.Create(new StringReader(xml),
                    new XmlReaderSettings { XmlResolver = null });
                info = (UpdateInfoEventArgs)new XmlSerializer(typeof(UpdateInfoEventArgs)).Deserialize(reader);
            }
            finally
            {
                AutoUpdater.BaseUri = previousBaseUri;
            }

            if (string.IsNullOrEmpty(info.CurrentVersion) || string.IsNullOrEmpty(info.DownloadURL))
                return false;
            version = info.CurrentVersion;
            downloadUrl = info.DownloadURL;
            changelogUrl = info.ChangelogURL ?? "";
            return true;
        }

        // JSON releases API (github / gitee style)
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("ToDo-Updater");
        var json = http.GetStringAsync(source.Url).Result;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        version = (root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null)?.TrimStart('v') ?? "";
        body = root.TryGetProperty("body", out var b) ? b.GetString() : "";
        downloadUrl = FindZipUrl(root);
        return !string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(downloadUrl);
    }

    private static string FindZipUrl(JsonElement root)
    {
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() : "";
                if (name?.EndsWith(".zip") == true)
                {
                    if (asset.TryGetProperty("browser_download_url", out var u)) return u.GetString() ?? "";
                    if (asset.TryGetProperty("download_url", out var u2)) return u2.GetString() ?? "";
                }
            }
        }
        if (root.TryGetProperty("browser_download_url", out var url)) return url.GetString() ?? "";
        return "";
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
