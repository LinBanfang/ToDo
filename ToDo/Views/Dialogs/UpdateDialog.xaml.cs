using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using AutoUpdaterDotNET;
using ToDo.Services;

namespace ToDo.Views.Dialogs;

public partial class UpdateDialog : Window
{
    private readonly UpdateInfoEventArgs _args;

    public UpdateDialog(UpdateInfoEventArgs args, string? releaseBody)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => TitleBarService.Apply(this);
        _args = args;
        VersionText.Text = $"v{args.CurrentVersion}";
        BodyText.Text = string.IsNullOrWhiteSpace(releaseBody) ? "" : releaseBody.Trim();
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        DownloadBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;

        var zipPath = Path.Combine(Path.GetTempPath(), $"ToDo-{_args.CurrentVersion}.zip");
        await DownloadAsync(_args.DownloadURL, zipPath);

        LaunchUpdater(zipPath);
        Close();
        Application.Current.Shutdown();
    }

    private async Task DownloadAsync(string url, string target)
    {
        using var client = new HttpClient();
        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result;
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? 0;
        await using var content = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(target);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await content.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            if (total > 0)
                Progress.Value = downloaded * 100.0 / total;
        }
    }

    /// <summary>
    /// Launches a hidden PowerShell script that waits for this app to exit, extracts
    /// the downloaded zip into the install directory and relaunches ToDo.exe.
    /// Paths are passed base64-encoded so no quoting/escaping can break the script.
    /// </summary>
    private static void LaunchUpdater(string zipPath)
    {
        var installDir = AppDomain.CurrentDomain.BaseDirectory;
        var exe = Path.Combine(installDir, "ToDo.exe");

        string b64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

        var script = """
            $ErrorActionPreference = 'Stop'
            $zip = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__ZIP__'))
            $dir = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__DIR__'))
            $exe = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('__EXE__'))
            for ($i = 0; $i -lt 30 -and (Get-Process -Name ToDo -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 300 }
            Start-Sleep -Milliseconds 500
            $temp = Join-Path ([IO.Path]::GetTempPath()) ('ToDoUpd-' + [Guid]::NewGuid().ToString('N'))
            try {
                Expand-Archive -LiteralPath $zip -DestinationPath $temp -Force
                Copy-Item -Path (Join-Path $temp '*') -Destination $dir -Recurse -Force
                Start-Process -FilePath $exe
            } catch {
                [IO.File]::WriteAllText((Join-Path ([IO.Path]::GetTempPath()) 'ToDoUpdateError.log'), $_.Exception.ToString())
            }
            Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
            """;

        script = script
            .Replace("__ZIP__", b64(zipPath))
            .Replace("__DIR__", b64(installDir))
            .Replace("__EXE__", b64(exe));

        var scriptPath = Path.Combine(Path.GetTempPath(), $"ToDoUpdater-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(scriptPath, script, new UTF8Encoding(true));

        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File \"{scriptPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Process.Start(psi);
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (Version.TryParse(_args.CurrentVersion, out var v))
            AutoUpdater.PersistenceProvider.SetSkippedVersion(v);
        Close();
    }

    private void RemindLater_Click(object sender, RoutedEventArgs e)
    {
        AutoUpdater.PersistenceProvider.SetRemindLater(DateTime.Now.AddDays(2));
        Close();
    }
}
