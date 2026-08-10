namespace ToDo.Models;

/// <summary>
/// A list's background opacity ("背景强弱"), keyed by list id. Local-only, never synced
/// (ADR-014): it lives in its own untracked collection because a field on TaskList would
/// be overwritten by the sync layer's whole-entity list upsert — and because opacity is a
/// display preference tied to a local-only asset (image bytes, or a locally-chosen color),
/// so it has no meaning on a device that doesn't share that asset. Keyed by list id so
/// Upsert keeps exactly one row per list; a missing row reads back as the default 100.
/// </summary>
public class ListBackgroundSetting
{
    /// <summary>Equals the list id so Upsert keeps exactly one row per list.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Background opacity in percent (20..100). 100 = the theme's natural look;
    /// lower values fade the background toward the window background. Only stored when it
    /// differs from the default, so a missing row means 100.</summary>
    public int OpacityPercent { get; set; } = 100;
}
