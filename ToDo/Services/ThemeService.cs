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
    private static readonly Uri LightUri = new("pack://application:,,,/Styles/FluentColors.xaml");

    /// <summary>The currently installed code-built dark dictionary (Source is null, so
    /// it can't be identified by URI like the light one).</summary>
    private static ResourceDictionary? _darkDictionary;

    public static void Apply(string theme)
    {
        var merged = Application.Current.Resources.MergedDictionaries;
        var target = theme == "Dark" ? CreateDarkDictionary() : LoadLightDictionary();

        // Replace the active theme dictionary in place: the light one is found by
        // Source URI, the code-built dark one by reference (its Source is null).
        for (int i = 0; i < merged.Count; i++)
        {
            bool isThemeDict = (merged[i].Source != null && merged[i].Source.OriginalString.Contains("FluentColors"))
                || ReferenceEquals(merged[i], _darkDictionary);
            if (isThemeDict)
            {
                merged.RemoveAt(i);
                merged.Insert(i, target);
                _darkDictionary = theme == "Dark" ? target : null;
                return;
            }
        }

        merged.Insert(0, target);
        _darkDictionary = theme == "Dark" ? target : null;

        // The OS-drawn title bar doesn't follow DynamicResource; recolor every
        // open window via DWM so it matches the just-swapped palette.
        TitleBarService.ApplyAll();
    }

    private static ResourceDictionary LoadLightDictionary() => new() { Source = LightUri };

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
        d["TextPrimaryBrush"] = Brush("#FFFFFF");
        d["TextSecondaryBrush"] = Brush("#C8C6C4");
        d["TextDisabledBrush"] = Brush("#797775");
        d["TextAccentBrush"] = Brush("#4CC2FF");
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
