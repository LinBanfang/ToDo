using System.IO;
using System.Net.Http;
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
        _args = args;
        VersionText.Text = $"{Loc.UpdateAvailable} v{args.CurrentVersion}";
        BodyText.Text = string.IsNullOrWhiteSpace(releaseBody) ? "" : releaseBody.Trim();
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        DownloadBtn.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;

        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\Downloads";
        Directory.CreateDirectory(downloads);
        var target = Path.Combine(downloads, Path.GetFileName(new Uri(_args.DownloadURL).AbsolutePath));

        using var client = new HttpClient();
        using var response = client.GetAsync(_args.DownloadURL, HttpCompletionOption.ResponseHeadersRead).Result;
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

        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"");
        Close();
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
