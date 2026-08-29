using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class WebDavTokenRepository(Models.ApplicationContext context) : IWebDavTokenRepository
{
    public async Task<IReadOnlyList<WebDavToken>> GetAllOrderedAsync(CancellationToken cancellationToken)
    {
        var entities = await context.WebDavTokens
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<WebDavToken?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        var entity = await context.WebDavTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Username == username, cancellationToken);
        return entity?.ToRecord();
    }

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        return context.WebDavTokens.AnyAsync(t => t.Username == username, cancellationToken);
    }

    public async Task AddAsync(WebDavToken token, CancellationToken cancellationToken)
    {
        await context.WebDavTokens.AddAsync(token.ToEntity(), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RevokeByIdAsync(
        Guid id,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        var entity = await context.WebDavTokens.FindAsync([id], cancellationToken);
        if (entity is null) return false;

        if (entity.RevokedAt is null)
            entity.RevokedAt = revokedAt;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
