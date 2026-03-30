using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SpecialsFilter;

/// <summary>
/// Runs after every full library scan and removes Season 0 (Specials) items from
/// libraries and shows configured to do so. Only removes from the Jellyfin
/// database — no files are ever deleted from disk.
/// For single-library refreshes, see <see cref="LibraryItemAddedHandler"/>.
/// </summary>
public class SpecialsRemovalTask : ILibraryPostScanTask
{
    private readonly SpecialsRemovalService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpecialsRemovalTask"/> class.
    /// </summary>
    public SpecialsRemovalTask(SpecialsRemovalService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
        => _service.RunForAllLibraries(progress, cancellationToken);
}

