using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class MetadataRecognitionRuleRepository(Models.ApplicationContext context)
    : IMetadataRecognitionRuleRepository
{
    public async Task<IReadOnlyList<MetadataRecognitionRule>> ListAsync(CancellationToken cancellationToken) =>
        (await context.Set<Models.MetadataRecognitionRule>().AsNoTracking()
            .OrderByDescending(rule => rule.CreatedAt).ToListAsync(cancellationToken)).Select(ToRecord).ToList();

    public async Task<MetadataRecognitionRule?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.Set<Models.MetadataRecognitionRule>().AsNoTracking()
            .SingleOrDefaultAsync(rule => rule.Id == id, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<bool> SaveAsync(MetadataRecognitionRule rule, long? expectedRevision,
        CancellationToken cancellationToken)
    {
        var entity = expectedRevision is null ? new Models.MetadataRecognitionRule { Id = rule.Id }
            : await context.Set<Models.MetadataRecognitionRule>()
                .SingleOrDefaultAsync(candidate => candidate.Id == rule.Id, cancellationToken);
        if (entity is null || (expectedRevision is not null && entity.Revision != expectedRevision)) return false;
        entity.Name = rule.Name;
        entity.Enabled = rule.Enabled;
        entity.Revision = rule.Revision;
        entity.SourceFeedId = rule.SourceFeedId;
        entity.TitlePattern = rule.TitlePattern;
        entity.SubtitleGroup = rule.SubtitleGroup;
        entity.TmdbId = rule.TmdbId;
        entity.FixedSeason = rule.FixedSeason;
        entity.EpisodeOffset = rule.EpisodeOffset;
        entity.CanonicalGroupName = rule.CanonicalGroupName;
        entity.CreatedFromItemId = rule.CreatedFromItemId;
        entity.CreatedAt = rule.CreatedAt;
        entity.EffectiveFrom = rule.EffectiveFrom;
        if (expectedRevision is null) context.Add(entity);
        try { await context.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { context.Entry(entity).State = EntityState.Detached; return false; }
    }

    public async Task<IReadOnlyList<AnimationInfo>> GetCandidatesAsync(Guid? sourceFeedId, int take,
        CancellationToken cancellationToken) =>
        (await context.AnimationInfo.AsNoTracking().Include(info => info.Animation).Include(info => info.Group)
            .Where(info => info.MediaLibraryMissingSince == null
                           && (sourceFeedId == null || info.SourceFeedId == sourceFeedId))
            .OrderByDescending(info => info.IngestedAt).ThenByDescending(info => info.Id)
            .Take(take).ToListAsync(cancellationToken)).Select(info => info.ToRecord()).ToList();

    public async Task<IReadOnlyList<MetadataRecognitionHit>> GetHitsAsync(Guid? animationInfoId,
        CancellationToken cancellationToken) =>
        await context.Set<Models.MetadataRecognitionHit>().AsNoTracking()
            .Where(hit => animationInfoId == null || hit.AnimationInfoId == animationInfoId)
            .OrderByDescending(hit => hit.AppliedAt).Take(50)
            .Select(hit => new MetadataRecognitionHit(hit.Id, hit.RuleId, hit.RuleName, hit.RuleRevision,
                hit.AnimationInfoId, hit.Title, hit.ItemRevision, hit.AppliedAt)).ToListAsync(cancellationToken);

    internal static MetadataRecognitionRule ToRecord(Models.MetadataRecognitionRule rule) => new(
        rule.Id, rule.Name, rule.Enabled, rule.Revision, rule.SourceFeedId, rule.TitlePattern,
        rule.SubtitleGroup, rule.TmdbId, rule.FixedSeason, rule.EpisodeOffset, rule.CanonicalGroupName,
        rule.CreatedFromItemId, rule.CreatedAt, rule.EffectiveFrom);
}
