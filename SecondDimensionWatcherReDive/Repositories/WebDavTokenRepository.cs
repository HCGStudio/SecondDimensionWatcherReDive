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

    public async Task<bool> UpdateHashAsync(
        Guid id,
        string expectedHash,
        string newHash,
        CancellationToken cancellationToken)
    {
        var updated = await context.WebDavTokens
            .Where(token => token.Id == id && token.TokenHash == expectedHash)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.TokenHash, newHash),
                cancellationToken);
        return updated == 1;
    }

    public async Task<bool> RemoveByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.WebDavTokens.FindAsync([id], cancellationToken);
        if (entity is null) return false;

        context.WebDavTokens.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
