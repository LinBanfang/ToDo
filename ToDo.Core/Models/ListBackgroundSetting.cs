namespace ToDo.Models;

/// <summary>
/// A list's per-list theme display settings — background strength ("背景强弱") and card
/// opacity ("卡片不透明度"), keyed by list id. Local-only, never synced (ADR-014): the row
/// lives in its own untracked collection because a field on TaskList would be overwritten
/// by the sync layer's whole-entity list upsert — and because both knobs are display
/// preferences tied to local-only assets (image bytes, or a locally-chosen color), so they
/// have no meaning on a device that doesn't share those assets. Keyed by list id so Upsert
/// keeps exactly one row per list; a missing row reads back as the defaults.
/// </summary>
public class ListBackgroundSetting
{
    /// <summary>Equals the list id so Upsert keeps exactly one row per list.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Background opacity in percent (20..100). 100 = the theme's natural look;
    /// lower values fade the background toward the window background. Only stored when it
    /// differs from the default, so a missing row means 100.</summary>
    public int OpacityPercent { get; set; } = 100;

    /// <summary>Task-card opacity in percent (30..100) — the alpha of TaskCardBrush /
    /// TaskCardHoverBrush for this list. 65 matches the theme's default look; higher is
    /// more solid. Only stored when it differs from the default (0 = a row written before
    /// this field existed, read back as 65).</summary>
    public int CardOpacityPercent { get; set; } = 65;
}
