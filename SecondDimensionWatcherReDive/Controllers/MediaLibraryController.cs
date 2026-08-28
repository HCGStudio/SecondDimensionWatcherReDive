using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Services;
using SecondDimensionWatcherReDive.Utils.FileStore;

namespace SecondDimensionWatcherReDive.Controllers;

[ApiController]
[Route("api/media-library/sources")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
internal sealed class MediaLibraryController(
    IMediaLibrarySourceRepository repository,
    IMediaLibraryScanQueue scanQueue,
    IOptionsMonitor<MediaLibraryOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSources(CancellationToken cancellationToken)
    {
        var sources = await repository.GetAllAsync(cancellationToken);
        return Ok(sources.Select(ToResponse).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> CreateSource(
        [FromBody] External.CreateMediaLibrarySourceRequest request,
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        if (!TryNormalizeDirectory(
                request.Path,
                currentOptions.AllowedRoots,
                currentOptions.DownloadRoot,
                out var path,
                out var error))
            return BadRequest(new { error });

        var sources = await repository.GetAllAsync(cancellationToken);
        if (sources.Any(source => MediaLibraryPath.PathsOverlap(source.Path, path)))
            return Conflict(new { error = "The path is already covered by a configured media library source." });

        var source = new MediaLibrarySource(
            Guid.NewGuid(),
            path,
            request.IsMonitoring,
            DateTimeOffset.UtcNow,
            LastScanAt: null,
            LastError: null,
            LastImportedCount: 0,
            LastUpdatedCount: 0,
            LastRemovedCount: 0,
            LastSkippedCount: 0);
        try
        {
            if (!await repository.TryAddAsync(source, cancellationToken))
                return Conflict(new { error = "The path is already covered by a configured media library source." });
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation
            })
        {
            return Conflict(new { error = "The media library source already exists." });
        }

        scanQueue.Enqueue(source.Id);
        return CreatedAtAction(
            nameof(GetSources),
            routeValues: null,
            ToResponse(source));
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> UpdateSource(
        [FromRoute] Guid id,
        [FromBody] External.UpdateMediaLibrarySourceRequest request,
        CancellationToken cancellationToken)
    {
        return await repository.SetMonitoringAsync(id, request.IsMonitoring, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    [HttpPost("{id:guid}/scan")]
    public async Task<IActionResult> ScanSource(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (await repository.FindByIdAsync(id, cancellationToken) is null)
            return NotFound();

        var queued = scanQueue.Enqueue(id);
        return Accepted(new External.QueueMediaLibraryScanResponse(queued));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSource(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        if (scanQueue.IsQueuedOrRunning(id))
            return Conflict(new { error = "Wait for the active media library scan to finish before removing it." });

        var result = await repository.TryRemoveByIdAsync(id, cancellationToken);
        return result switch
        {
            MediaLibrarySourceRemoveResult.Removed => NoContent(),
            MediaLibrarySourceRemoveResult.NotFound => NotFound(),
            MediaLibrarySourceRemoveResult.Busy => Conflict(new
            {
                error = "Wait for the active media library scan to finish before removing it."
            }),
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, null)
        };
    }

    private External.MediaLibrarySourceResponse ToResponse(MediaLibrarySource source) => new(
        source.Id,
        source.Path,
        source.IsMonitoring,
        source.CreatedAt,
        source.LastScanAt,
        source.LastError,
        source.LastImportedCount,
        source.LastUpdatedCount,
        source.LastRemovedCount,
        source.LastSkippedCount,
        scanQueue.IsQueuedOrRunning(source.Id));

    private static bool TryNormalizeDirectory(
        string? requestedPath,
        IReadOnlyList<string> allowedRoots,
        string? downloadRoot,
        out string path,
        out string error)
    {
        path = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            error = "A media library path is required.";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(requestedPath))
            {
                error = "The media library path must be an absolute server path.";
                return false;
            }

            if (!Directory.Exists(requestedPath))
            {
                error = "The media library path does not exist or is not a directory.";
                return false;
            }

            path = MediaLibraryPath.ResolveExistingPath(requestedPath);

            if (!MediaLibraryPath.IsAllowed(path, allowedRoots))
            {
                error = "The media library path is outside the configured allowed roots.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(downloadRoot)
                && MediaLibraryPath.PathsOverlap(path, downloadRoot))
            {
                error = "The media library path cannot overlap the managed download directory.";
                return false;
            }

            // Force one enumeration so an unreadable mount fails at configuration time.
            using var enumerator = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
            _ = enumerator.MoveNext();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException
                                          or UnauthorizedAccessException)
        {
            error = "The media library path is invalid or cannot be read.";
            return false;
        }
    }

}
