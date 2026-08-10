using System.Windows;
using System.Windows.Media;

namespace ToDo.Services;

/// <summary>
/// Swaps the FluentColors theme dictionary at runtime. All theme brush
/// references use {DynamicResource ...}, so replacing the dictionary updates
/// every live window without a rebuild.
/// </summary>
public static class ThemeService
{
    /// <summary>The currently installed code-built dark dictionary (Source is null, so
    /// it can't be identified by URI like the light one).</summary>
    private static ResourceDictionary? _darkDictionary;

    /// <summary>Card opacity (30..100) applied to the shared task-card brushes — a display
    /// preference driven by the ACTIVE list's per-list setting (ADR-014). Reapplied on theme
    /// switches and whenever the active list / its card opacity changes, so the brushes always
    /// match the list on screen.</summary>
    private static int _cardOpacity = 65;

    public static void Apply(string theme)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        var target = theme == "Dark" ? CreateDarkDictionary() : LoadLightDictionary();
        ApplyCardOpacityTo(target, theme, _cardOpacity);

        // Replace the active theme dictionary in place: the light one is found by
        // Source URI, the code-built dark one by reference (its Source is null).
        int index = merged.Count;
        for (int i = 0; i < merged.Count; i++)
        {
            if (IsThemeDictionary(merged[i])) { index = i; break; }
        }

        if (index < merged.Count)
        {
            merged.RemoveAt(index);
            merged.Insert(index, target);
        }
        else
        {
            merged.Insert(0, target);
        }
        _darkDictionary = theme == "Dark" ? target : null;

        // The OS-drawn title bar doesn't follow DynamicResource; recolor every
        // open window via DWM so it matches the just-swapped palette.
        TitleBarService.ApplyAll();
    }

    /// <summary>Re-tints the live task-card brushes at the new opacity. Called when the
    /// active list's card opacity changes (list switch, or the theme dialog's OK) — the
    /// brushes are shared DynamicResources, so replacing them recolors every open window
    /// without a rebuild. Safe in unit tests (no WPF Application): only the cached value
    /// is updated there, applied on the next Apply.</summary>
    public static void SetCardOpacity(int percent)
    {
        _cardOpacity = percent;
        if (Application.Current == null) return;
        var dict = FindActiveThemeDictionary();
        if (dict != null)
            ApplyCardOpacityTo(dict, SettingsService.Current.Theme, percent);
    }

    private static ResourceDictionary? FindActiveThemeDictionary()
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        for (int i = 0; i < merged.Count; i++)
            if (IsThemeDictionary(merged[i])) return merged[i];
        return null;
    }

    private static bool IsThemeDictionary(ResourceDictionary d) =>
        (d.Source != null && d.Source.OriginalString.Contains("FluentColors"))
        || ReferenceEquals(d, _darkDictionary);

    /// <summary>Sets the two card brushes at the given opacity (hover a bit stronger). The
    /// base RGB differs per theme; alpha is the adjustable knob. Matches the original
    /// defaults exactly (65% → 0xA6, hover 85% → 0xD9), so the knob just varies that same
    /// look per list.</summary>
    private static void ApplyCardOpacityTo(ResourceDictionary d, string theme, int percent)
    {
        var dark = theme == "Dark";
        d["TaskCardBrush"] = CardBrush(dark ? "#2B2B2B" : "#FFFFFF", percent);
        d["TaskCardHoverBrush"] = CardBrush(dark ? "#333333" : "#F3F2F1", Math.Min(percent + 20, 100));
    }

    private static SolidColorBrush CardBrush(string hex, int alphaPercent)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        c.A = (byte)Math.Round(255 * alphaPercent / 100.0);
        return new SolidColorBrush(c);
    }

    /// <summary>Builds the light dictionary from the FluentColors.xaml resource. The pack URI
    /// is constructed lazily (not in a static field initializer): parsing "pack://application:,,,"
    /// needs WPF's registered pack scheme, which isn't present in unit tests that reference this
    /// type without a running Application — but they never call Apply, so the URI is only ever
    /// parsed here, in-app.</summary>
    private static ResourceDictionary LoadLightDictionary() =>
        new() { Source = new Uri("pack://application:,,,/Styles/FluentColors.xaml") };

    /// <summary>
    /// Dark palette mirroring the keys of Styles/FluentColors.xaml. Built in code
    /// so it needs no separate embedded resource; keep the two in sync.
    /// </summary>
    private static ResourceDictionary CreateDarkDictionary()
    {
        var d = new ResourceDictionary();
        d["NeutralWhite"] = Brush("#FFFFFF");
        d["NeutralGray10"] = Brush("#FFFFFF");
        d["NeutralGray20"] = Brush("#1B1B1B");
        d["NeutralGray30"] = Brush("#292929");
        d["NeutralGray40"] = Brush("#333333");
        d["NeutralGray50"] = Brush("#3C3C3C");
        d["NeutralGray60"] = Brush("#4C4C4C");
        d["NeutralGray90"] = Brush("#5C5C5C");
        d["NeutralGray110"] = Brush("#797775");
        d["NeutralGray130"] = Brush("#8A8886");
        d["NeutralGray150"] = Brush("#C8C6C4");
        d["NeutralGray160"] = Brush("#E1DFDD");
        d["NeutralGray190"] = Brush("#FFFFFF");
        d["AccentBlue"] = Brush("#4CC2FF");
        d["AccentBlueLight"] = Brush("#264F78");
        d["AccentGreen"] = Brush("#6CCB5F");
        d["AccentRed"] = Brush("#F1707B");
        d["AccentOrange"] = Brush("#F7630C");
        d["AccentYellow"] = Brush("#FCE100");
        d["AppBackgroundBrush"] = Brush("#202020");
        d["SidebarBackgroundBrush"] = Brush("#1B1B1B");
        d["SidebarBorderBrush"] = Brush("#333333");
        d["CardBackgroundBrush"] = Brush("#2B2B2B");
        d["CardHoverBrush"] = Brush("#333333");
        d["CardSelectedBrush"] = Brush("#264F78");
        d["ListBackgroundMaskBrush"] = Brush("#4D000000");
        d["TextPrimaryBrush"] = Brush("#FFFFFF");
        d["TextSecondaryBrush"] = Brush("#C8C6C4");
        d["TextDisabledBrush"] = Brush("#797775");
        d["TextAccentBrush"] = Brush("#4CC2FF");
        d["TaskMetaBrush"] = Brush("#A19F9D");
        d["BorderLightBrush"] = Brush("#333333");
        d["DividerBrush"] = Brush("#3C3C3C");
        d["AccentRedBrush"] = Brush("#F1707B");
        d["AccentGreenBrush"] = Brush("#6CCB5F");
        d["FluentFont"] = new FontFamily("Microsoft YaHei UI");
        return d;
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));
}
