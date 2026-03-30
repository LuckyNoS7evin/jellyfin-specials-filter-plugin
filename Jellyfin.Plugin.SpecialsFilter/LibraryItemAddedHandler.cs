using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpecialsFilter;

/// <summary>
/// Subscribes to <see cref="ILibraryManager.ItemAdded"/> to handle specials removal
/// when individual library items are added during a single-library refresh.
/// Complements <see cref="SpecialsRemovalTask"/> which only runs on full scans.
/// </summary>
public class LibraryItemAddedHandler : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly SpecialsRemovalService _service;
    private readonly ILogger<LibraryItemAddedHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryItemAddedHandler"/> class.
    /// </summary>
    public LibraryItemAddedHandler(
        ILibraryManager libraryManager,
        SpecialsRemovalService service,
        ILogger<LibraryItemAddedHandler> logger)
    {
        _libraryManager = libraryManager;
        _service = service;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        return Task.CompletedTask;
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs args)
    {
        // Only react to Season 0 (Specials) episodes being added.
        if (args.Item is not Episode episode) return;
        if (episode.ParentIndexNumber != 0) return;

        var config = Plugin.Instance?.Configuration;
        if (config is null) return;

        var libraryRemoveMap = config.LibrarySettings
            .ToDictionary(s => s.LibraryId, s => s.RemoveSpecials);
        var showOverrideMap = config.ShowSettings
            .Where(s => s.Handling != SpecialsHandling.Default)
            .ToDictionary(s => s.ShowId, s => s.Handling);
        var episodeBlacklist = config.EpisodeBlacklist.ToHashSet();

        bool anyEnabled = libraryRemoveMap.Values.Any(v => v)
            || showOverrideMap.Values.Any(v => v == SpecialsHandling.Remove)
            || episodeBlacklist.Count > 0;

        if (!anyEnabled) return;

        _logger.LogDebug(
            "[SpecialsFilter] Special episode added: '{Name}' (series {SeriesId}), checking removal config.",
            episode.Name, episode.SeriesId);

        _service.RunForSeries([episode.SeriesId], libraryRemoveMap, showOverrideMap, episodeBlacklist);
    }
}
