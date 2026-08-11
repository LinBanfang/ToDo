using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToDo.Converters;
using ToDo.Models;
using ToDo.Services;

namespace ToDo.ViewModels;

public partial class MainViewModel
{
    // ─── Active list background theme (ADR-014) ───────────
    /// <summary>Brush painting the task-area background for the active list's theme.
    /// Null when there's no theme, searching, or no active list — the window's
    /// AppBackgroundBrush then shows through. Rebuilt lazily and re-raised explicitly
    /// (SetListTheme / OnActiveListChanged / OnSearchQueryChanged): LoadLists re-points
    /// ActiveList only when the instance differs, so an in-place theme edit would not
    /// re-trigger a converter bound to ActiveList's fields.</summary>
    public Brush? ListBackgroundBrush
    {
        get
        {
            if (IsSearching || ActiveList == null) return null;
            // Per-list opacity ("背景强弱", local-only): lower fades the background toward
            // the window background. Baked into the brush so solid colors and images share
            // one knob; the readability mask is left untouched.
            var opacity = _db.GetListThemeSettings(ActiveList.Id).Background / 100.0;
            return ActiveList.BackgroundType switch
            {
                ListBackgroundType.Solid => BuildSolidBrush(ActiveList.BackgroundColor, opacity),
                ListBackgroundType.Image => BuildImageBrush(ActiveList.Id, opacity),
                _ => null,
            };
        }
    }

    /// <summary>True when the active list has an image background (dimming mask visible).
    /// Hidden during search so the global background shows across lists.</summary>
    public bool ListBackgroundMaskVisible =>
        !IsSearching && ActiveList?.BackgroundType == ListBackgroundType.Image;

    /// <summary>The active list's card opacity (30..100) — the knob applied to the shared
    /// TaskCardBrush/TaskCardHoverBrush (ADR-014). Default 65 when unset.</summary>
    private int ActiveCardOpacity =>
        ActiveList == null ? 65 : _db.GetListThemeSettings(ActiveList.Id).Card;

    /// <summary>Header title text color for the active list: true = light (white) text,
    /// false = dark (near-black) text, null = no themed background → the app theme's normal
    /// text color. Auto mode (the default) recommends from the background — solid luminance
    /// or the image band behind the header (TitleTextEstimator); a fixed 深色/浅色 choice
    /// overrides it. Re-raised on list switch / search / theme edit / theme change.</summary>
    public bool? HeaderTitleLight
    {
        get
        {
            if (IsSearching || ActiveList == null) return null;
            if (ActiveList.BackgroundType == ListBackgroundType.None) return null;
            var mode = _db.GetListThemeSettings(ActiveList.Id).TitleMode;
            if (mode == 1) return false;   // 深色文字
            if (mode == 2) return true;    // 浅色文字
            var darkTheme = SettingsService.Current.Theme == "Dark";
            var image = ActiveList.BackgroundType == ListBackgroundType.Image
                ? _db.GetListBackgroundData(ActiveList.Id) : null;
            return TitleTextEstimator.Recommend(darkTheme, ActiveList.BackgroundType,
                ActiveList.BackgroundColor, image);
        }
    }

    private Brush? BuildSolidBrush(string hex, double opacity)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try
        {
            var brush = new SolidColorBrush(ColorParser.ParseColor(hex)) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }
        catch { return null; }
    }

    private Brush? BuildImageBrush(string listId, double opacity)
    {
        var bytes = _db.GetListBackgroundData(listId);
        if (bytes == null || bytes.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;   // read fully before the stream closes
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill, Opacity = opacity };
            brush.Freeze();
            return brush;
        }
        catch { return null; }
    }

}
