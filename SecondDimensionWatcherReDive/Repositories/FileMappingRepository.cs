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
        // Raw SQL: the chained .Select(...).Select(...).Distinct() over IndexOf/Substring
        // does not dedupe under Npgsql 10 — three mappings under the same root produced
        // three duplicate rows at the WebDAV root. split_part is unambiguous and runs
        // server-side so we don't load every mapping.
        var rows = await context.Database
            .SqlQueryRaw<RootEntryRow>(
                """
                SELECT DISTINCT
                    split_part("VirtualPath", '/', 2) AS "Name",
                    position('/' IN substring("VirtualPath" FROM 2)) > 0 AS "IsDirectory"
                FROM "FileMappings"
                WHERE length("VirtualPath") > 1 AND "VirtualPath" LIKE '/%'
                """)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new RootEntry(r.Name, r.IsDirectory)).ToList();
    }

    private sealed record RootEntryRow(string Name, bool IsDirectory);

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
