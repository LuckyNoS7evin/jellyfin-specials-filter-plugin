using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SpecialsFilter;

/// <summary>
/// Specials Filter plugin entry point.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Specials Filter";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("c7f2d3a1-4b8e-4f9d-a2c5-1e3f7b9d0e2a");

    /// <inheritdoc />
    public override string Description => "Removes specials (Season 0) from configured TV show libraries after each library scan. Configurable per library and per show.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "specialsfilter",
                EmbeddedResourcePath = $"{GetType().Namespace}.Pages.configurationpage.html",
                EnableInMainMenu = false,
                MenuSection = "server",
                MenuIcon = "filter_alt",
                DisplayName = "Specials Filter"
            }
        ];
    }
}
