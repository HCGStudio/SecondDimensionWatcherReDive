using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public class FileMappingRepository(Models.ApplicationContext context) : IFileMappingRepository
{
    public async Task AddRangeAsync(IReadOnlyList<FileMapping> mappings, CancellationToken cancellationToken)
    {
        if (mappings.Count == 0) return;
        await context.FileMappings.AddRangeAsync(mappings.Select(m => m.ToEntity()), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<FileMapping?> FindByVirtualPathAsync(string virtualPath, CancellationToken cancellationToken)
    {
        var entity = await context.FileMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.VirtualPath == virtualPath, cancellationToken);
        return entity?.ToRecord();
    }

    public async Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(string virtualPathPrefix, CancellationToken cancellationToken)
    {
        var pattern = EscapeLikePattern(virtualPathPrefix) + "%";
        var entities = await context.FileMappings
            .AsNoTracking()
            .Where(m => EF.Functions.Like(m.VirtualPath, pattern, "\\"))
            .ToListAsync(cancellationToken);
        return entities.Select(e => e.ToRecord()).ToList();
    }

    public async Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken)
    {
        var rows = await context.FileMappings
            .AsNoTracking()
            .Where(m => m.VirtualPath.Length > 1 && m.VirtualPath.StartsWith("/"))
            .Select(m => new
            {
                Path = m.VirtualPath,
                NextSlash = m.VirtualPath.IndexOf('/', 1)
            })
            .Select(x => new
            {
                Name = x.NextSlash < 0
                    ? x.Path.Substring(1)
                    : x.Path.Substring(1, x.NextSlash - 1),
                IsDirectory = x.NextSlash > 0
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows.Select(r => new RootEntry(r.Name, r.IsDirectory)).ToList();
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    public async Task<bool> VirtualPathExistsAsync(string virtualPath, CancellationToken cancellationToken)
    {
        return await context.FileMappings
            .AsNoTracking()
            .AnyAsync(m => m.VirtualPath == virtualPath, cancellationToken);
    }

    public async Task<bool> ExistsForAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken)
    {
        return await context.FileMappings
            .AsNoTracking()
            .AnyAsync(m => m.AnimationInfoId == animationInfoId, cancellationToken);
    }

    public async Task RemoveByAnimationInfoAsync(Guid animationInfoId, CancellationToken cancellationToken)
    {
        await context.FileMappings
            .Where(m => m.AnimationInfoId == animationInfoId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
