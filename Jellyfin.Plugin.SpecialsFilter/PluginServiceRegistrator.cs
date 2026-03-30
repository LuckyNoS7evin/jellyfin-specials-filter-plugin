using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Jellyfin.Plugin.SpecialsFilter;

/// <summary>
/// Registers plugin services with the Jellyfin DI container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<SpecialsRemovalService>();
        serviceCollection.AddSingleton<ILibraryPostScanTask, SpecialsRemovalTask>();
        serviceCollection.AddHostedService<LibraryItemAddedHandler>();
    }
}

