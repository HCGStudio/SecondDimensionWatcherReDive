using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Repositories;

public class FileNameRegexRuleRepository(Models.ApplicationContext context) : IFileNameRegexRuleRepository
{
    public async Task<IReadOnlyList<FileNameRegexRule>> GetForAnimationAsync(
        Guid animationId,
        CancellationToken cancellationToken)
    {
        var entities = await context.FileNameRegexRules
            .AsNoTracking()
            .Where(rule => rule.AnimationId == animationId)
            .OrderByDescending(rule => rule.CreatedAt)
            .ThenByDescending(rule => rule.Id)
            .Take(FileNameRegexMatcher.MaxRulesPerAnimation)
            .ToListAsync(cancellationToken);
        return entities.Select(rule => rule.ToRecord()).ToList();
    }

    public async Task<FileNameRegexRule> GetOrAddAsync(
        FileNameRegexRule rule,
        CancellationToken cancellationToken)
    {
        var existing = await context.FileNameRegexRules
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.AnimationId == rule.AnimationId
                             && candidate.Pattern == rule.Pattern,
                cancellationToken);
        if (existing is not null) return existing.ToRecord();

        var entity = rule.ToEntity();
        await context.FileNameRegexRules.AddAsync(entity, cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return rule;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                          { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another inference call inserted the same scoped pattern between the read and write.
            context.Entry(entity).State = EntityState.Detached;
            var concurrent = await context.FileNameRegexRules
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate => candidate.AnimationId == rule.AnimationId
                                 && candidate.Pattern == rule.Pattern,
                    cancellationToken);
            if (concurrent is null) throw;
            return concurrent.ToRecord();
        }
    }
}
