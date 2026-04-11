using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpecialsFilter.Api;

/// <summary>
/// Library info returned to the configuration UI.
/// </summary>
public class LibraryInfo
{
    /// <summary>Gets or sets the library item ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the library display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether specials are removed from this library.</summary>
    public bool RemoveSpecials { get; set; }
}

/// <summary>
/// Show info returned to the configuration UI.
/// </summary>
public class ShowInfo
{
    /// <summary>Gets or sets the series item ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the series display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the handling override for this show (0=Default, 1=Remove, 2=Keep).</summary>
    public int Handling { get; set; }

    /// <summary>Gets or sets whether this show has at least one missing (virtual) special episode.</summary>
    public bool HasMissingSpecials { get; set; }
}

/// <summary>
/// Individual special episode info returned to the configuration UI.
/// </summary>
public class EpisodeInfo
{
    /// <summary>Gets or sets the episode item ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the episode display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the episode number within the specials season.</summary>
    public int? IndexNumber { get; set; }

    /// <summary>Gets or sets the episode premiere date.</summary>
    public DateTime? PremiereDate { get; set; }

    /// <summary>Gets or sets whether this episode is on the removal blacklist.</summary>
    public bool Blacklisted { get; set; }

    /// <summary>Gets or sets whether this episode has no media file on disk (virtual/missing item).</summary>
    public bool Missing { get; set; }
}

/// <summary>
/// Summary of the currently configured removals, resolved to library/show/episode names.
/// </summary>
public class SettingsSummaryResponse
{
    /// <summary>Gets or sets the configured libraries and their relevant show/episode details.</summary>
    public List<SummaryLibraryInfo> Libraries { get; set; } = [];
}

/// <summary>
/// Summary details for a single TV library.
/// </summary>
public class SummaryLibraryInfo
{
    /// <summary>Gets or sets the library item ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the library display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this library removes specials by default.</summary>
    public bool RemoveSpecials { get; set; }

    /// <summary>Gets or sets the configured shows within this library that matter for the summary.</summary>
    public List<SummaryShowInfo> Shows { get; set; } = [];
}

/// <summary>
/// Summary details for a single show.
/// </summary>
public class SummaryShowInfo
{
    /// <summary>Gets or sets the show item ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the show display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the current configured handling for this show.</summary>
    public int Handling { get; set; }

    /// <summary>Gets or sets the blacklisted episodes for this show.</summary>
    public List<SummaryEpisodeInfo> Episodes { get; set; } = [];
}

/// <summary>
/// Summary details for a blacklisted special episode.
/// </summary>
public class SummaryEpisodeInfo
{
    /// <summary>Gets or sets the episode item ID.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the episode display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the episode number within the specials season.</summary>
    public int? IndexNumber { get; set; }

    /// <summary>Gets or sets the episode premiere date.</summary>
    public DateTime? PremiereDate { get; set; }
}

/// <summary>
/// Request body for saving plugin configuration.
/// </summary>
public class SaveConfigRequest
{
    /// <summary>Gets or sets the per-library settings.</summary>
    public LibrarySetting[] LibrarySettings { get; set; } = [];

    /// <summary>Gets or sets the per-show override settings.</summary>
    public ShowSetting[] ShowSettings { get; set; } = [];

    /// <summary>Gets or sets individual special episode IDs that should always be removed.</summary>
    public string[] EpisodeBlacklist { get; set; } = [];
}

/// <summary>
/// API controller for the Specials Filter plugin configuration.
/// </summary>
[ApiController]
[Route("SpecialsFilter")]
[Authorize(Policy = "RequiresElevation")]
public class SpecialsFilterController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<SpecialsFilterController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpecialsFilterController"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    public SpecialsFilterController(ILibraryManager libraryManager, ILogger<SpecialsFilterController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Gets all TV libraries with their current specials-removal settings.
    /// </summary>
    /// <returns>List of libraries with settings.</returns>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<LibraryInfo>> GetLibraries()
    {
        var config = Plugin.Instance!.Configuration;
        var settingsMap = config.LibrarySettings.ToDictionary(s => s.LibraryId, s => s.RemoveSpecials);

        var libraries = _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType == CollectionTypeOptions.tvshows)
            .Select(f => new LibraryInfo
            {
                Id = f.ItemId,
                Name = f.Name,
                RemoveSpecials = settingsMap.TryGetValue(f.ItemId, out var val) && val
            })
            .ToList();

        return Ok(libraries);
    }

    /// <summary>
    /// Gets all TV shows in the specified library with their current override settings.
    /// </summary>
    /// <param name="libraryId">The library item ID.</param>
    /// <returns>List of shows with their handling overrides.</returns>
    [HttpGet("Libraries/{libraryId}/Shows")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<IEnumerable<ShowInfo>> GetShows([FromRoute] string libraryId)
    {
        if (!Guid.TryParse(libraryId, out var libraryGuid))
        {
            return BadRequest("Invalid library ID.");
        }

        var config = Plugin.Instance!.Configuration;
        var showSettingsMap = config.ShowSettings.ToDictionary(s => s.ShowId, s => s.Handling);

        // Single query for all virtual (missing) Season 0 episodes in the library.
        var showsWithMissingSpecials = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Episode],
            AncestorIds = [libraryGuid],
            IsVirtualItem = true,
            ParentIndexNumber = 0,
            Recursive = true
        })
        .OfType<Episode>()
        .Select(e => e.SeriesId.ToString())
        .ToHashSet();

        var shows = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Series],
            AncestorIds = [libraryGuid],
            Recursive = true
        })
        .OfType<Series>()
        .Select(s => new ShowInfo
        {
            Id = s.Id.ToString(),
            Name = s.Name ?? string.Empty,
            Handling = (int)(showSettingsMap.TryGetValue(s.Id.ToString(), out var h) ? h : SpecialsHandling.Default),
            HasMissingSpecials = showsWithMissingSpecials.Contains(s.Id.ToString())
        })
        .OrderBy(s => s.Name)
        .ToList();

        return Ok(shows);
    }

    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    /// <returns>Current library, show and episode settings.</returns>
    [HttpGet("Config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SaveConfigRequest> GetConfig()
    {
        var config = Plugin.Instance!.Configuration;
        return Ok(new SaveConfigRequest
        {
            LibrarySettings = config.LibrarySettings,
            ShowSettings = config.ShowSettings,
            EpisodeBlacklist = config.EpisodeBlacklist
        });
    }

    /// <summary>
    /// Gets a named summary of the current effective removal configuration.
    /// </summary>
    /// <returns>Configured libraries, shows and blacklisted episodes.</returns>
    [HttpGet("Summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SettingsSummaryResponse> GetSummary()
    {
        var config = Plugin.Instance!.Configuration;
        var libraryRemoveMap = config.LibrarySettings.ToDictionary(s => s.LibraryId, s => s.RemoveSpecials);

        // Show IDs that have a non-default handling override.
        var showOverrides = config.ShowSettings
            .Where(s => s.Handling != SpecialsHandling.Default)
            .ToDictionary(s => s.ShowId, s => s.Handling);

        // Resolve the episode blacklist to series IDs upfront so we can look them
        // up efficiently per library below.  We look up episodes via GetItemById
        // only here (not per library), which is safe because episodes carry their
        // own SeriesId property without needing a loaded parent chain.
        var blacklistBySeriesId = new Dictionary<string, List<SummaryEpisodeInfo>>();
        foreach (var episodeIdStr in config.EpisodeBlacklist)
        {
            if (!Guid.TryParse(episodeIdStr, out var episodeGuid)
                || _libraryManager.GetItemById(episodeGuid) is not Episode episode)
            {
                continue;
            }

            var seriesId = episode.SeriesId.ToString();
            if (!blacklistBySeriesId.TryGetValue(seriesId, out var epList))
            {
                epList = [];
                blacklistBySeriesId[seriesId] = epList;
            }

            epList.Add(new SummaryEpisodeInfo
            {
                Id = episode.Id.ToString(),
                Name = episode.Name ?? string.Empty,
                IndexNumber = episode.IndexNumber,
                PremiereDate = episode.PremiereDate
            });
        }

        bool hasConfiguredShows = showOverrides.Count > 0 || blacklistBySeriesId.Count > 0;
        var result = new List<SummaryLibraryInfo>();

        foreach (var folder in _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType == CollectionTypeOptions.tvshows))
        {
            bool libraryRemoves = libraryRemoveMap.TryGetValue(folder.ItemId, out var rm) && rm;

            var summaryShows = new List<SummaryShowInfo>();

            // When the library removes everything we don't need to enumerate shows.
            if (!libraryRemoves && hasConfiguredShows
                && Guid.TryParse(folder.ItemId, out var libraryGuid))
            {
                // Use AncestorIds so Jellyfin resolves the hierarchy correctly —
                // GetTopParent() on items loaded via GetItemById is unreliable.
                var showsInLibrary = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.Series],
                    AncestorIds = [libraryGuid],
                    Recursive = true
                })
                .OfType<Series>()
                .Where(s => showOverrides.ContainsKey(s.Id.ToString())
                         || blacklistBySeriesId.ContainsKey(s.Id.ToString()))
                .ToList();

                foreach (var show in showsInLibrary)
                {
                    var showId = show.Id.ToString();
                    var handling = showOverrides.TryGetValue(showId, out var h) ? h : SpecialsHandling.Default;

                    // Only include episodes if the show isn't already removing everything.
                    var episodes = new List<SummaryEpisodeInfo>();
                    if (handling != SpecialsHandling.Remove
                        && blacklistBySeriesId.TryGetValue(showId, out var epList))
                    {
                        episodes = epList
                            .OrderBy(e => e.IndexNumber ?? int.MaxValue)
                            .ThenBy(e => e.Name)
                            .ToList();
                    }

                    if (handling == SpecialsHandling.Remove || episodes.Count > 0)
                    {
                        summaryShows.Add(new SummaryShowInfo
                        {
                            Id = showId,
                            Name = show.Name ?? string.Empty,
                            Handling = (int)handling,
                            Episodes = episodes
                        });
                    }
                }

                summaryShows = summaryShows.OrderBy(s => s.Name).ToList();
            }

            if (libraryRemoves || summaryShows.Count > 0)
            {
                result.Add(new SummaryLibraryInfo
                {
                    Id = folder.ItemId,
                    Name = folder.Name,
                    RemoveSpecials = libraryRemoves,
                    Shows = summaryShows
                });
            }
        }

        return Ok(new SettingsSummaryResponse
        {
            Libraries = result.OrderBy(l => l.Name).ToList()
        });
    }

    /// <summary>
    /// Gets all special (Season 0) episodes for a given show, with blacklist status.
    /// </summary>
    /// <param name="showId">The series item ID.</param>
    /// <returns>List of special episodes.</returns>
    [HttpGet("Shows/{showId}/Specials")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<IEnumerable<EpisodeInfo>> GetSpecials([FromRoute] string showId)
    {
        if (!Guid.TryParse(showId, out var showGuid))
        {
            return BadRequest("Invalid show ID.");
        }

        var config = Plugin.Instance!.Configuration;
        var blacklist = config.EpisodeBlacklist.ToHashSet();

        var specialsSeasons = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Season],
            ParentId = showGuid,
            Recursive = false
        })
        .OfType<Season>()
        .Where(s => s.IndexNumber == 0)
        .ToList();

        if (specialsSeasons.Count == 0)
        {
            return Ok(Array.Empty<EpisodeInfo>());
        }

        var episodes = new List<EpisodeInfo>();
        foreach (var season in specialsSeasons)
        {
            var eps = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Episode],
                ParentId = season.Id,
                Recursive = false
            })
            .OfType<Episode>();

            foreach (var ep in eps)
            {
                episodes.Add(new EpisodeInfo
                {
                    Id = ep.Id.ToString(),
                    Name = ep.Name ?? string.Empty,
                    IndexNumber = ep.IndexNumber,
                    PremiereDate = ep.PremiereDate,
                    Blacklisted = blacklist.Contains(ep.Id.ToString()),
                    Missing = ep.LocationType == LocationType.Virtual
                });
            }
        }

        return Ok(episodes.OrderBy(e => e.IndexNumber).ThenBy(e => e.Name).ToList());
    }

    /// <summary>
    /// Saves the plugin configuration.
    /// </summary>
    /// <param name="request">Updated library and show settings.</param>
    /// <returns>No content on success.</returns>
    [HttpPost("Config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult SaveConfig([FromBody] SaveConfigRequest request)
    {
        if (request is null)
        {
            return BadRequest();
        }

        var config = Plugin.Instance!.Configuration;

        // Strip Default-handling show entries — they add no value and keep config clean.
        config.LibrarySettings = request.LibrarySettings ?? [];
        config.ShowSettings = (request.ShowSettings ?? [])
            .Where(s => s.Handling != SpecialsHandling.Default)
            .ToArray();
        config.EpisodeBlacklist = request.EpisodeBlacklist ?? [];

        Plugin.Instance.SaveConfiguration();

        _logger.LogInformation(
            "[SpecialsFilter] Configuration saved. {LibCount} library setting(s), {ShowCount} show override(s), {EpCount} episode blacklist entry(ies).",
            config.LibrarySettings.Length,
            config.ShowSettings.Length,
            config.EpisodeBlacklist.Length);

        return NoContent();
    }
}
