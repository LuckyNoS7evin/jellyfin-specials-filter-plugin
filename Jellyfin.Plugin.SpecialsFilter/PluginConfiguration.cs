using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SpecialsFilter;

/// <summary>
/// Controls how specials (Season 0) are handled for a library or show.
/// </summary>
public enum SpecialsHandling
{
    /// <summary>Inherit the setting from the parent library.</summary>
    Default = 0,

    /// <summary>Always remove specials.</summary>
    Remove = 1,

    /// <summary>Always keep specials, even if the library says to remove them.</summary>
    Keep = 2
}

/// <summary>
/// Per-library specials removal setting.
/// </summary>
public class LibrarySetting
{
    /// <summary>Gets or sets the library (virtual folder) item ID.</summary>
    public string LibraryId { get; set; } = string.Empty;

    /// <summary>Gets or sets whether specials should be removed from this library after scan.</summary>
    public bool RemoveSpecials { get; set; }
}

/// <summary>
/// Per-show specials handling override.
/// </summary>
public class ShowSetting
{
    /// <summary>Gets or sets the series item ID (Guid string).</summary>
    public string ShowId { get; set; } = string.Empty;

    /// <summary>Gets or sets the handling mode for this show.</summary>
    public SpecialsHandling Handling { get; set; } = SpecialsHandling.Default;
}

/// <summary>
/// Plugin configuration for the Specials Filter plugin.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets per-library specials removal settings.
    /// Libraries not listed here default to keeping specials.
    /// </summary>
    public LibrarySetting[] LibrarySettings { get; set; } = [];

    /// <summary>
    /// Gets or sets per-show overrides. A show set to <see cref="SpecialsHandling.Default"/>
    /// inherits from its library. Remove/Keep explicitly override the library setting.
    /// </summary>
    public ShowSetting[] ShowSettings { get; set; } = [];

    /// <summary>
    /// Gets or sets individual special episode IDs that should always be removed,
    /// regardless of the library or show setting.
    /// </summary>
    public string[] EpisodeBlacklist { get; set; } = [];
}
