using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Utils.FileStore;
namespace SecondDimensionWatcherReDive.Repositories;

public sealed class LibraryCompletionRepository(Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> options) : ILibraryCompletionRepository
{
    public async Task<IReadOnlyList<AnimationInfo>> GetSeasonReleasesAsync(string tmdbId, int season,
        CancellationToken cancellationToken) => (await context.AnimationInfo.AsNoTracking()
        .Include(x => x.Animation).Include(x => x.Group)
        .Where(x => x.Animation != null && x.Animation.TmdbId == tmdbId && x.Season == season &&
                    x.MediaLibraryMissingSince == null && !x.IsRetiredRelease)
        .OrderByDescending(x => x.PublishTime).ToListAsync(cancellationToken)).Select(x => x.ToRecord()).ToList();

    public async Task<IReadOnlySet<Guid>> GetMappedReleaseIdsAsync(string tmdbId, int season,
        CancellationToken cancellationToken) => await context.AnimationInfo.AsNoTracking()
        .Where(x => x.Animation != null && x.Animation.TmdbId == tmdbId && x.Season == season &&
            x.MediaLibraryMissingSince == null && !x.IsRetiredRelease &&
            context.FileMappings.Any(mapping => mapping.AnimationInfoId == x.Id))
        .Select(x => x.Id).ToHashSetAsync(cancellationToken);

    public async Task<bool> TryClaimEpisodeAsync(string tmdbId, int season, int episode, Guid releaseId,
        Guid claimId, CancellationToken cancellationToken) => await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
    {
        await using var write = new Models.ApplicationContext(options);
        await using var transaction = await write.Database.BeginTransactionAsync(cancellationToken);
        await MappingTransactionLock.AcquireAsync(write, cancellationToken);
        var siblings = write.AnimationInfo.Where(x => x.Animation != null && x.Animation.TmdbId == tmdbId &&
            x.Season == season && x.Episode == episode && x.MediaLibraryMissingSince == null && !x.IsRetiredRelease);
        if (!await siblings.AnyAsync(x => x.Id == releaseId &&
            (x.MetadataStatus == MetadataReviewStatus.Identified || x.MetadataStatus == MetadataReviewStatus.Reviewed), cancellationToken) ||
            await siblings.AnyAsync(x => x.IsDownloadTracked || x.IsDownloadFinished, cancellationToken))
            return false;
        var claim = await write.Set<Models.EpisodeAcquisition>().FindAsync([tmdbId, season, episode], cancellationToken);
        if (claim != null && claim.ExpiresAt > DateTimeOffset.UtcNow) return false;
        if (claim == null)
        {
            claim = new Models.EpisodeAcquisition { TmdbId = tmdbId, Season = season, Episode = episode };
            write.Add(claim);
        }
        claim.ClaimId = claimId;
        claim.ReleaseId = releaseId;
        claim.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await write.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    });
    public async Task ReleaseClaimAsync(string tmdbId, int season, int episode, Guid claimId,
        CancellationToken cancellationToken) => await context.Set<Models.EpisodeAcquisition>()
        .Where(x => x.TmdbId == tmdbId && x.Season == season && x.Episode == episode && x.ClaimId == claimId)
        .ExecuteDeleteAsync(cancellationToken);

    internal static Task<bool> HasActiveClaimAsync(Models.ApplicationContext writeContext, Guid releaseId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return writeContext.Set<Models.EpisodeAcquisition>()
            .AnyAsync(claim => claim.ReleaseId == releaseId && claim.ExpiresAt > now, cancellationToken);
    }
}
