using System.Drawing;
using System.IO;
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

        // Convert PNG to ICO at startup
        GenerateIcoFromPng();

        Database = new DatabaseService();
        ViewModel = new MainViewModel(Database);

        var mainWindow = new MainWindow { DataContext = ViewModel };
        try
        {
            string[] candidates = { "app.png", "app.ico" };
            foreach (var name in candidates)
            {
                try
                {
                    mainWindow.Icon = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri($"pack://application:,,,/Resources/{name}"));
                    break;
                }
                catch { }
            }
        }
        catch { }
        mainWindow.Show();
    }

    private static void GenerateIcoFromPng()
    {
        var pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Resources", "app.png");
        var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
        var pkgIcoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Resources", "app.ico");

        if (File.Exists(pngPath) && !File.Exists(pkgIcoPath))
        {
            try
            {
                using var png = new Bitmap(pngPath);
                using var ms = new MemoryStream();
                // Write BMP header
                var bmpSize = 40 + png.Width * png.Height * 4;
                var icoHeader = new byte[22];
                icoHeader[0] = 0; icoHeader[2] = 1; icoHeader[4] = 1; // ICO, 1 image
                icoHeader[6] = (byte)png.Width; icoHeader[7] = (byte)png.Height;
                icoHeader[14] = 32; // 32-bit
                var dataSize = bmpSize + png.Width * png.Height / 8;
                BitConverter.GetBytes(dataSize).CopyTo(icoHeader, 8);
                BitConverter.GetBytes(22).CopyTo(icoHeader, 18);
                ms.Write(icoHeader, 0, 22);

                // BMP info header + pixel data
                using var bmpMs = new MemoryStream();
                png.Save(bmpMs, System.Drawing.Imaging.ImageFormat.Bmp);
                var bmpData = bmpMs.ToArray();
                // Skip to pixel data (offset 54 for 32-bit BMP)
                ms.Write(bmpData, 14, 40); // info header
                for (int y = png.Height - 1; y >= 0; y--)
                    for (int x = 0; x < png.Width; x++)
                    {
                        var c = png.GetPixel(x, y);
                        ms.WriteByte(c.B); ms.WriteByte(c.G); ms.WriteByte(c.R); ms.WriteByte(c.A);
                    }
                // AND mask
                var andMask = new byte[png.Width * png.Height / 8];
                ms.Write(andMask, 0, andMask.Length);

                File.WriteAllBytes(icoPath, ms.ToArray());
                File.WriteAllBytes(pkgIcoPath, ms.ToArray());
            }
            catch { }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Database?.Dispose();
        base.OnExit(e);
    }
}
