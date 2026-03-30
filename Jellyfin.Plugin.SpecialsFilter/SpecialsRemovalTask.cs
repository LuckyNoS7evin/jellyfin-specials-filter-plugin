using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpecialsFilter;

/// <summary>
/// Runs after every library scan and removes Season 0 (Specials) items from
/// libraries and shows configured to do so. Only removes from the Jellyfin
/// database — no files are ever deleted from disk.
/// </summary>
public class SpecialsRemovalTask : ILibraryPostScanTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SpecialsRemovalTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpecialsRemovalTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    public SpecialsRemovalTask(ILibraryManager libraryManager, ILogger<SpecialsRemovalTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            progress.Report(100);
            return;
        }

        var libraryRemoveMap = config.LibrarySettings
            .ToDictionary(s => s.LibraryId, s => s.RemoveSpecials);

        var showOverrideMap = config.ShowSettings
            .Where(s => s.Handling != SpecialsHandling.Default)
            .ToDictionary(s => s.ShowId, s => s.Handling);

        var episodeBlacklist = config.EpisodeBlacklist.ToHashSet();

        bool anyLibraryEnabled = libraryRemoveMap.Values.Any(v => v);
        bool anyShowRemoveOverride = config.ShowSettings.Any(s => s.Handling == SpecialsHandling.Remove);
        bool anyEpisodeBlacklisted = episodeBlacklist.Count > 0;

        if (!anyLibraryEnabled && !anyShowRemoveOverride && !anyEpisodeBlacklisted)
        {
            _logger.LogDebug("[SpecialsFilter] Nothing configured. Skipping.");
            progress.Report(100);
            return;
        }

        // Pre-build a set of show IDs that own at least one blacklisted episode,
        // so we can process those shows even when their library/show setting says "keep".
        var showsWithBlacklist = BuildShowsWithBlacklist(episodeBlacklist);

        var tvLibraries = _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType == CollectionTypeOptions.tvshows)
            .ToList();

        int total = Math.Max(tvLibraries.Count, 1);
        int done = 0;

        foreach (var library in tvLibraries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool libraryRemovesSpecials = libraryRemoveMap.TryGetValue(library.ItemId, out var libVal) && libVal;

            if (!libraryRemovesSpecials && !anyShowRemoveOverride && !anyEpisodeBlacklisted)
            {
                done++;
                progress.Report((double)done / total * 100);
                continue;
            }

            if (!Guid.TryParse(library.ItemId, out var libraryGuid))
            {
                _logger.LogWarning("[SpecialsFilter] Could not parse library ID '{Id}', skipping.", library.ItemId);
                done++;
                progress.Report((double)done / total * 100);
                continue;
            }

            var shows = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Series],
                AncestorIds = [libraryGuid],
                Recursive = true
            })
            .OfType<Series>()
            .ToList();

            _logger.LogDebug("[SpecialsFilter] Processing library '{Name}' ({Count} shows).", library.Name, shows.Count);

            foreach (var show in shows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool removeAll = ResolveRemoveSpecials(show.Id.ToString(), libraryRemovesSpecials, showOverrideMap);
                bool hasBlacklistedEpisodes = showsWithBlacklist.Contains(show.Id.ToString());

                if (!removeAll && !hasBlacklistedEpisodes) continue;

                RemoveSpecialsFromShow(show, removeAll, episodeBlacklist);
            }

            done++;
            progress.Report((double)done / total * 100);
        }

        await Task.CompletedTask;
        progress.Report(100);
        _logger.LogInformation("[SpecialsFilter] Post-scan specials removal complete.");
    }

    private HashSet<string> BuildShowsWithBlacklist(HashSet<string> episodeBlacklist)
    {
        var result = new HashSet<string>();
        foreach (var episodeIdStr in episodeBlacklist)
        {
            if (!Guid.TryParse(episodeIdStr, out var episodeGuid)) continue;
            if (_libraryManager.GetItemById(episodeGuid) is Episode ep)
            {
                result.Add(ep.SeriesId.ToString());
            }
        }

        return result;
    }

    private static bool ResolveRemoveSpecials(
        string showId,
        bool libraryRemovesSpecials,
        Dictionary<string, SpecialsHandling> showOverrideMap)
    {
        if (showOverrideMap.TryGetValue(showId, out var override_))
        {
            return override_ == SpecialsHandling.Remove;
        }

        return libraryRemovesSpecials;
    }

    private void RemoveSpecialsFromShow(Series show, bool removeAll, HashSet<string> episodeBlacklist)
    {
        var specialsSeasons = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Season],
            ParentId = show.Id,
            Recursive = false
        })
        .OfType<Season>()
        .Where(s => s.IndexNumber == 0)
        .ToList();

        if (specialsSeasons.Count == 0) return;

        foreach (var season in specialsSeasons)
        {
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                ParentId = season.Id,
                Recursive = false
            }).ToList();

            int removedCount = 0;
            foreach (var episode in episodes)
            {
                bool shouldRemove = removeAll || episodeBlacklist.Contains(episode.Id.ToString());
                if (!shouldRemove) continue;

                try
                {
                    _libraryManager.DeleteItem(episode, new DeleteOptions { DeleteFileLocation = false });
                    _logger.LogDebug("[SpecialsFilter] Removed special episode '{Name}' from '{Show}'.", episode.Name, show.Name);
                    removedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SpecialsFilter] Failed to remove episode '{Name}'.", episode.Name);
                }
            }

            // Only remove the season container when every episode inside it was removed.
            if (removedCount == episodes.Count && episodes.Count > 0)
            {
                try
                {
                    _libraryManager.DeleteItem(season, new DeleteOptions { DeleteFileLocation = false });
                    _logger.LogInformation("[SpecialsFilter] Removed Specials season from '{Show}'.", show.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[SpecialsFilter] Failed to remove Specials season from '{Show}'.", show.Name);
                }
            }
            else if (removedCount > 0)
            {
                _logger.LogInformation(
                    "[SpecialsFilter] Removed {Count}/{Total} special episode(s) from '{Show}' (season kept, remaining episodes preserved).",
                    removedCount, episodes.Count, show.Name);
            }
        }
    }
}
