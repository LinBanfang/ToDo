using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToDo.Models;

namespace ToDo.Services;

/// <summary>
/// Recommends whether a list's header title should be light or dark text, given its
/// background. Solid colors use sRGB-weighted luminance; images sample the band that
/// actually sits behind the header under the default window layout (UniformToFill crop,
/// centered), so the recommendation tracks what the eye sees rather than the whole image.
/// Shared by the ViewModel (live header) and the theme dialog (recommendation hint).
/// </summary>
public static class TitleTextEstimator
{
    // Default layout (window 1200×720, sidebar 280, splitter 5, detail pane closed): the
    // main content column these describe is exactly what the per-list background fills.
    private const double ContentWidth = 1200 - 280 - 5;
    private const double ContentHeight = 720 - 32;   // minus the OS title bar
    private const double HeaderHeight = 56;          // header padding (20+6) + 28px title row

    /// <summary>True = light (white) title text, False = dark (near-black) title text,
    /// null = no theme to judge (background None, or unusable input) — the caller then
    /// falls back to the app theme's normal text color.</summary>
    public static bool? Recommend(bool darkTheme, ListBackgroundType type, string? solidHex, byte[]? imageBytes)
    {
        switch (type)
        {
            case ListBackgroundType.Solid:
                if (string.IsNullOrEmpty(solidHex)) return null;
                try { return Luminance((Color)ColorConverter.ConvertFromString(solidHex)) < 0.5; }
                catch { return null; }
            case ListBackgroundType.Image:
                if (imageBytes is not { Length: > 0 }) return null;
                return SampleHeaderLuminance(imageBytes, darkTheme) < 0.5;
            default:
                return null;
        }
    }

    /// <summary>sRGB-weighted brightness of a color (0 = black .. 1 = white).</summary>
    private static double Luminance(Color c) =>
        (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    /// <summary>Average brightness of the image band behind the header, after the theme's
    /// dimming mask is applied (white 30% in light, black 30% in dark). The image is decoded
    /// small (aspect preserved) and the UniformToFill crop is computed at the default layout,
    /// so only the visible top band is sampled.</summary>
    private static double SampleHeaderLuminance(byte[] bytes, bool darkTheme)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 240;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();

            int iw = bmp.PixelWidth, ih = bmp.PixelHeight;
            if (iw <= 0 || ih <= 0) return 0.5;

            // UniformToFill: scale to cover the content area, crop the overflow, centered.
            double s = Math.Max(ContentWidth / iw, ContentHeight / ih);
            double visW = ContentWidth / s, visH = ContentHeight / s;
            double visX = (iw - visW) / 2.0, visY = (ih - visH) / 2.0;
            double bandH = HeaderHeight / s;

            var rect = new Int32Rect(
                (int)Math.Round(visX), (int)Math.Round(visY),
                (int)Math.Round(visW), (int)Math.Max(1, Math.Round(bandH)));
            rect.X = Math.Clamp(rect.X, 0, iw);
            rect.Y = Math.Clamp(rect.Y, 0, ih);
            rect.Width = Math.Max(1, Math.Min(rect.Width, iw - rect.X));
            rect.Height = Math.Max(1, Math.Min(rect.Height, ih - rect.Y));
            if (rect.Width <= 0 || rect.Height <= 0) return 0.5;

            var cropped = new CroppedBitmap(bmp, rect);
            var flat = new FormatConvertedBitmap(cropped, PixelFormats.Bgr24, null, 0);
            var stride = flat.PixelWidth * 3;
            var pixels = new byte[stride * flat.PixelHeight];
            flat.CopyPixels(pixels, stride, 0);

            double sum = 0;
            int count = flat.PixelWidth * flat.PixelHeight;
            for (int i = 0; i < pixels.Length; i += 3)
            {
                sum += (0.299 * pixels[i + 2] + 0.587 * pixels[i + 1] + 0.114 * pixels[i]) / 255.0;
            }
            double l = count == 0 ? 0.5 : sum / count;

            // The readability mask the header renders over the image.
            return darkTheme ? l * 0.7 : l * 0.7 + 0.3;
        }
        catch
        {
            return 0.5;   // undecodable image → neutral; a fixed choice still works
        }
    }
}
