using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpecialsFilter;

/// <summary>
/// Shared service that contains the core specials-removal logic.
/// Used by both the post-scan task (full scans) and the library-changed consumer (single-library refreshes).
/// </summary>
public class SpecialsRemovalService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SpecialsRemovalService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpecialsRemovalService"/> class.
    /// </summary>
    public SpecialsRemovalService(ILibraryManager libraryManager, ILogger<SpecialsRemovalService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Runs specials removal across all configured TV libraries.
    /// Called after a full library scan.
    /// </summary>
    public async Task RunForAllLibraries(IProgress<double> progress, CancellationToken cancellationToken)
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

                ProcessShow(show, removeAll, episodeBlacklist);
            }

            done++;
            progress.Report((double)done / total * 100);
        }

        await Task.CompletedTask;
        progress.Report(100);
        _logger.LogInformation("[SpecialsFilter] Post-scan specials removal complete.");
    }

    /// <summary>
    /// Processes specials removal for a specific set of series IDs.
    /// Called when individual library items are added (single-library refresh).
    /// </summary>
    public void RunForSeries(
        IEnumerable<Guid> seriesIds,
        Dictionary<string, bool> libraryRemoveMap,
        Dictionary<string, SpecialsHandling> showOverrideMap,
        HashSet<string> episodeBlacklist)
    {
        foreach (var seriesId in seriesIds)
        {
            if (_libraryManager.GetItemById(seriesId) is not Series show) continue;

            // GetTopParent() walks up to the CollectionFolder (IsTopParent=true), which is the library.
            var libraryId = show.GetTopParent()?.Id.ToString();
            bool libraryRemovesSpecials = libraryId is not null
                && libraryRemoveMap.TryGetValue(libraryId, out var libVal) && libVal;

            bool removeAll = ResolveRemoveSpecials(show.Id.ToString(), libraryRemovesSpecials, showOverrideMap);
            bool hasBlacklistedEpisodes = episodeBlacklist.Count > 0
                && BuildShowsWithBlacklist(episodeBlacklist).Contains(show.Id.ToString());

            if (!removeAll && !hasBlacklistedEpisodes) continue;

            ProcessShow(show, removeAll, episodeBlacklist);
        }
    }

    /// <summary>
    /// Removes specials from a single show according to the resolved settings.
    /// </summary>
    public void ProcessShow(Series show, bool removeAll, HashSet<string> episodeBlacklist)
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
                    "[SpecialsFilter] Removed {Count}/{Total} special episode(s) from '{Show}' (season kept).",
                    removedCount, episodes.Count, show.Name);
            }
        }
    }

    /// <summary>
    /// Builds a set of series IDs that have at least one blacklisted episode.
    /// </summary>
    public HashSet<string> BuildShowsWithBlacklist(HashSet<string> episodeBlacklist)
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

    /// <summary>
    /// Resolves whether specials should be removed for a show, applying per-show overrides over the library default.
    /// </summary>
    public static bool ResolveRemoveSpecials(
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
}
