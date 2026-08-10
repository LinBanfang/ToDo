using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToDo.Models;
using ToDo.Services;
using Xunit;

namespace ToDo.Tests;

/// <summary>
/// Exercises TitleTextEstimator: solid colors are judged by sRGB-weighted luminance, images
/// by sampling the band that sits behind the header under the default layout (after the
/// theme's readability mask). True = light (white) text, False = dark, null = no judgment.
/// </summary>
public sealed class TitleTextEstimatorTests
{
    // ─── Solid colors ───

    [Fact]
    public void Solid_Dark_RecommendsLightText()
    {
        Assert.True(TitleTextEstimator.Recommend(darkTheme: true, ListBackgroundType.Solid, "#000000", null));
        Assert.True(TitleTextEstimator.Recommend(darkTheme: false, ListBackgroundType.Solid, "#0A0A0A", null));
    }

    [Fact]
    public void Solid_Light_RecommendsDarkText()
    {
        Assert.False(TitleTextEstimator.Recommend(darkTheme: true, ListBackgroundType.Solid, "#FFFFFF", null));
        Assert.False(TitleTextEstimator.Recommend(darkTheme: false, ListBackgroundType.Solid, "#F0F0F0", null));
    }

    [Fact]
    public void Solid_NoColorOrBadHex_IsNull()
    {
        Assert.Null(TitleTextEstimator.Recommend(true, ListBackgroundType.Solid, null, null));
        Assert.Null(TitleTextEstimator.Recommend(true, ListBackgroundType.Solid, "", null));
        Assert.Null(TitleTextEstimator.Recommend(true, ListBackgroundType.Solid, "not-a-color", null));
    }

    [Fact]
    public void None_IsNull()
    {
        Assert.Null(TitleTextEstimator.Recommend(true, ListBackgroundType.None, "#000000", null));
    }

    // ─── Images ───

    [Fact]
    public void Image_NoBytes_IsNull()
    {
        Assert.Null(TitleTextEstimator.Recommend(true, ListBackgroundType.Image, null, null));
        Assert.Null(TitleTextEstimator.Recommend(true, ListBackgroundType.Image, null, Array.Empty<byte>()));
    }

    [Fact]
    public void Image_Black_RecommendsLightText()
    {
        // Even after the dark theme's 30% black mask or the light theme's 30% white lift,
        // a black band stays well under the 0.5 threshold.
        Assert.True(TitleTextEstimator.Recommend(darkTheme: true, ListBackgroundType.Image, null, SolidPng(0, 0, 0)));
        Assert.True(TitleTextEstimator.Recommend(darkTheme: false, ListBackgroundType.Image, null, SolidPng(0, 0, 0)));
    }

    [Fact]
    public void Image_White_RecommendsDarkText()
    {
        // A white band stays well over the threshold in both themes.
        Assert.False(TitleTextEstimator.Recommend(darkTheme: true, ListBackgroundType.Image, null, SolidPng(255, 255, 255)));
        Assert.False(TitleTextEstimator.Recommend(darkTheme: false, ListBackgroundType.Image, null, SolidPng(255, 255, 255)));
    }

    [Fact]
    public void Image_MidGray_SplitsByThemeMask()
    {
        // Mid gray l=0.5. In the dark theme the 30% black mask drops it to 0.35 (< 0.5 →
        // light text); in the light theme the 30% white lift raises it to 0.65 (≥ 0.5 →
        // dark text). This is the exact readability trade-off the real header renders.
        var gray = SolidPng(128, 128, 128);
        Assert.True(TitleTextEstimator.Recommend(darkTheme: true, ListBackgroundType.Image, null, gray));
        Assert.False(TitleTextEstimator.Recommend(darkTheme: false, ListBackgroundType.Image, null, gray));
    }

    [Fact]
    public void Image_Undecodable_IsNeutral()
    {
        // Garbage bytes fall back to 0.5 → not light text in either theme (0.35 vs 0.65).
        var junk = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        Assert.False(TitleTextEstimator.Recommend(true, ListBackgroundType.Image, null, junk));
        Assert.False(TitleTextEstimator.Recommend(false, ListBackgroundType.Image, null, junk));
    }

    /// <summary>Encodes a small solid-color PNG in memory (WIC-backed, no Application needed).</summary>
    private static byte[] SolidPng(byte r, byte g, byte b)
    {
        const int size = 64;
        var bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgr32, null);
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;      // BGR32 byte order: B, G, R, A
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }
        bmp.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
