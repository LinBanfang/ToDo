using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;
using AutoUpdaterDotNET;
using ToDo.Views.Dialogs;

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
    private static UpdateSource[] _sources = Array.Empty<UpdateSource>();
    private static string? _latestReleaseBody;
    private static bool _manualCheck;

    /// <summary>First real failure from the last check, so a failed check can report
    /// the actual network error instead of the generic MissingFieldException.</summary>
    private static Exception? _lastCheckError;

    public static void Configure()
    {
        RefreshSources();

        AutoUpdater.InstalledVersion = typeof(UpdateService).Assembly.GetName().Version;
        AutoUpdater.PersistenceProvider = new JsonFilePersistenceProvider(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToDo", "updater.json"));
        AutoUpdater.ParseUpdateInfoEvent += ParseUpdateInfo;
        AutoUpdater.CheckForUpdateEvent += args =>
            Application.Current?.Dispatcher.Invoke(() => OnUpdateChecked(args));
    }

    /// <summary>Re-reads update sources from settings so edits apply to the next check.</summary>
    public static void RefreshSources()
    {
        // Update feeds come from settings.json (UpdateSources), tried in order;
        // an empty list falls back to the default GitHub + Gitee feeds.
        var configured = SettingsService.Current.UpdateSources;
        _sources = (configured is { Count: > 0 } ? configured : SettingsService.DefaultUpdateSources)
            .Select(s => new UpdateSource { Type = s.Type, Url = s.Url })
            .ToArray();
        DiagnosticLog.Info("update",
            "sources: " + string.Join(", ", _sources.Select(s => $"{s.Type} {Sanitize(s.Url)}")));
    }

    public static void CheckForUpdates()
    {
        DiagnosticLog.Info("update", "checking updates (startup)");
        RefreshSources();
        AutoUpdater.Start();
    }

    /// <summary>
    /// Manual check from the settings page: bypasses the "remind later" delay and
    /// surfaces the outcome (up to date / failed / update dialog) to the user.
    /// </summary>
    public static void CheckForUpdatesNow()
    {
        DiagnosticLog.Info("update", "checking updates (manual)");
        AutoUpdater.CancelRemindLater();
        AutoUpdater.Running = false; // a lingering background check must not swallow this
        _manualCheck = true;
        RefreshSources();
        AutoUpdater.Start();
    }

    private static void ParseUpdateInfo(ParseUpdateInfoEventArgs args)
    {
        _lastCheckError = null;
        foreach (var source in _sources)
        {
            try
            {
                if (TryGetLatest(source, out var version, out var downloadUrl, out var body, out var changelogUrl))
                {
                    _lastCheckError = null;
                    _latestReleaseBody = body;
                    DiagnosticLog.Info("update",
                        $"{source.Type} {Sanitize(source.Url)} -> version={version}, zip={Sanitize(downloadUrl)}");
                    args.UpdateInfo = new UpdateInfoEventArgs
                    {
                        CurrentVersion = version,
                        DownloadURL = downloadUrl,
                        ChangelogURL = changelogUrl,
                    };
                    return;
                }
            }
            catch (Exception ex)
            {
                // remember the first real failure so a failed check can report it
                _lastCheckError ??= ex;
                DiagnosticLog.Warn("update",
                    $"{source.Type} {Sanitize(source.Url)} -> {ex.GetType().Name}: {ex.Message}");
            }
        }

        DiagnosticLog.Error("update", "check failed: all update sources unavailable");
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
                info = new XmlSerializer(typeof(UpdateInfoEventArgs)).Deserialize(reader) as UpdateInfoEventArgs
                    ?? throw new InvalidDataException($"Update source '{source.Url}' returned an unusable appcast");
            }
            finally
            {
                AutoUpdater.BaseUri = previousBaseUri;
            }

            if (string.IsNullOrEmpty(info.CurrentVersion) || string.IsNullOrEmpty(info.DownloadURL))
                throw new InvalidDataException($"Update source '{source.Url}' returned no version or download URL");
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
        body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        downloadUrl = FindZipUrl(root);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
            throw new InvalidDataException($"Update source '{source.Url}' returned no version or download URL");
        return true;
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
        var owner = Application.Current?.MainWindow;
        if (owner == null) { _manualCheck = false; return; }

        // Manual checks from the settings page report the outcome; background checks stay silent
        if (_manualCheck)
        {
            _manualCheck = false;
            if (args.Error != null)
            {
                // Report the first real source error rather than the generic
                // MissingFieldException AutoUpdater throws for an all-sources failure.
                // When even that is missing (sources answered 200 with unusable data),
                // fall back to a friendly localized message instead of the cryptic
                // "attempted to access a non-existing field" text.
                var detail = _lastCheckError != null ? ErrorDetail(_lastCheckError) : Loc.UpdateSourceNoInfo;
                _lastCheckError = null;
                DiagnosticLog.Error("update", $"manual check failed: {detail}");
                FluentDialog.Show(Application.Current?.MainWindow, Loc.UpdateCheckFailed(detail),
                    Loc.Updates, MsgKind.Warning);
                return;
            }
            if (!args.IsUpdateAvailable)
            {
                DiagnosticLog.Info("update", $"manual check: up to date (latest {args.CurrentVersion})");
                FluentDialog.Show(Application.Current?.MainWindow, Loc.UpdateUpToDate(args.CurrentVersion),
                    Loc.Updates, MsgKind.Info);
                return;
            }
            DiagnosticLog.Info("update", $"manual check: update available {args.CurrentVersion}");
        }
        else if (args.Error != null || !args.IsUpdateAvailable)
        {
            // Background check stays silent in the UI but still logs the outcome
            if (args.Error != null)
                DiagnosticLog.Warn("update", $"startup check failed: {ErrorDetail(_lastCheckError ?? args.Error)}");
            else
                DiagnosticLog.Info("update", $"startup check: no update (latest {args.CurrentVersion})");
            _lastCheckError = null;
            return;
        }
        else
        {
            DiagnosticLog.Info("update", $"startup check: update available {args.CurrentVersion}");
        }

        var dialog = new Views.Dialogs.UpdateDialog(args, _latestReleaseBody) { Owner = owner };
        dialog.ShowDialog();
    }

    /// <summary>Redacts credentials (userinfo / access_token=) from a URL before logging.</summary>
    private static string Sanitize(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        var s = url;
        var schemeEnd = s.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            var at = s.IndexOf('@');
            if (at > schemeEnd) s = s.Substring(0, schemeEnd + 3) + "***@" + s.Substring(at + 1);
        }
        s = System.Text.RegularExpressions.Regex.Replace(
            s, "(access_token=)[^&\\s]+", "$1***", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return s;
    }

    /// <summary>Unwraps to the innermost exception so the user sees the real cause
    /// (e.g. "connection refused") instead of the HttpRequestException wrapper.</summary>
    private static string ErrorDetail(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null) current = current.InnerException;
        return current.Message;
    }
}
