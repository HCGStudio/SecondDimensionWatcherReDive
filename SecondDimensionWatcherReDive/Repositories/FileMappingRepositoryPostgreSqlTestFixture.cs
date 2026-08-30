using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using System.Data.Common;

namespace SecondDimensionWatcherReDive.Repositories;

/// <summary>
/// Owns PostgreSQL setup and inspection for repository integration tests without
/// exposing the EF context outside the repository implementation boundary.
/// </summary>
internal sealed class FileMappingRepositoryPostgreSqlTestFixture(string connectionString)
{
    private readonly DbContextOptions<Models.ApplicationContext> _contextOptions =
        new DbContextOptionsBuilder<Models.ApplicationContext>()
            .UseNpgsql(connectionString)
            .Options;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.MigrateAsync(
            "20260828164158_AddApplicationSettings",
            cancellationToken);
        var preexistingMapping = new Models.FileMapping
        {
            Id = Guid.NewGuid(),
            AnimationInfoId = Guid.NewGuid(),
            VirtualPath = "/backfill/existing/episode.mkv",
            PhysicalPath = "/physical/backfill.mkv",
            FileStore = "local"
        };
        var collisionInfo = new Models.AnimationInfo
        {
            Id = Guid.NewGuid(),
            Title = "migration collision",
            Description = "before migration"
        };
        var prefixMapping = new Models.FileMapping
        {
            Id = Guid.NewGuid(),
            AnimationInfoId = collisionInfo.Id,
            VirtualPath = "/unknown/foo",
            PhysicalPath = "/physical/prefix",
            FileStore = "local"
        };
        var operation = new Models.MetadataReviewOperation
        {
            Id = Guid.NewGuid(),
            AnimationInfoId = collisionInfo.Id,
            AnimationInfo = collisionInfo,
            State = MetadataReviewOperationState.Applied,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(13),
            ProposedAnimationTmdbId = "migration",
            ProposedAnimationName = "Migration",
            ProposedAnimationOriginalName = "Migration",
            ProposedDescription = "migration",
            MappingSnapshots =
            [
                new Models.MetadataReviewMappingSnapshot
                {
                    Id = Guid.NewGuid(),
                    Kind = MetadataReviewMappingKind.Proposed,
                    VirtualPath = prefixMapping.VirtualPath,
                    PhysicalPath = prefixMapping.PhysicalPath,
                    FileStore = prefixMapping.FileStore
                }
            ]
        };
        context.AddRange(
            preexistingMapping,
            collisionInfo,
            prefixMapping,
            new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = collisionInfo.Id,
                VirtualPath = "/unknown/foo/bar.mkv",
                PhysicalPath = "/physical/descendant",
                FileStore = "local"
            },
            new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = collisionInfo.Id,
                VirtualPath = "/unknown/foo (2)",
                PhysicalPath = "/physical/exact-candidate",
                FileStore = "local"
            },
            new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = collisionInfo.Id,
                VirtualPath = "/unknown/foo (3)/child.mkv",
                PhysicalPath = "/physical/directory-candidate",
                FileStore = "local"
            },
            new Models.PlaybackProgress
            {
                Id = Guid.NewGuid(),
                UserId = Guid.Empty,
                AnimationInfoId = collisionInfo.Id,
                VirtualPath = prefixMapping.VirtualPath,
                PositionSeconds = 42,
                DurationSeconds = 100,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            operation);
        await context.SaveChangesAsync(cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
        var backfilled = await context.FileSystemEntries
            .AsNoTracking()
            .Where(entry => entry.Path == "/backfill"
                            || entry.Path == "/backfill/existing"
                            || entry.Path == preexistingMapping.VirtualPath)
            .CountAsync(cancellationToken);
        if (backfilled != 3)
            throw new InvalidOperationException("File-system hierarchy backfill was incomplete.");
        BackfillVerified = true;

        var remappedPath = await context.FileMappings
            .Where(mapping => mapping.Id == prefixMapping.Id)
            .Select(mapping => mapping.VirtualPath)
            .SingleAsync(cancellationToken);
        var remappedProgressPath = await context.PlaybackProgresses
            .Where(progress => progress.AnimationInfoId == collisionInfo.Id
                               && progress.PositionSeconds == 42)
            .Select(progress => progress.VirtualPath)
            .SingleAsync(cancellationToken);
        var remappedSnapshotPath = await context.MetadataReviewMappingSnapshots
            .Where(snapshot => snapshot.OperationId == operation.Id)
            .Select(snapshot => snapshot.VirtualPath)
            .SingleAsync(cancellationToken);
        var oldPathEntry = await context.FileSystemEntries
            .AsNoTracking()
            .SingleAsync(entry => entry.Path == "/unknown/foo", cancellationToken);
        if (remappedPath != "/unknown/foo (4)"
            || remappedProgressPath != remappedPath
            || remappedSnapshotPath != remappedPath
            || !oldPathEntry.IsDirectory)
            throw new InvalidOperationException(
                "File/directory collision normalization did not preserve related state.");
        BackfillConflictResolved = true;
    }

    public bool BackfillVerified { get; private set; }

    public bool BackfillConflictResolved { get; private set; }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"FileMappings\", \"AnimationInfo\", \"Animations\", \"AnimationGroups\" RESTART IDENTITY CASCADE",
            cancellationToken);
    }

    public async Task<Guid> SeedDownloadedAnimationAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var info = new Models.AnimationInfo
        {
            Id = Guid.NewGuid(),
            Title = "integration test",
            IsDownloadFinished = true,
            FileStore = "local",
            StorePath = "/store/" + Guid.NewGuid().ToString("N")
        };
        context.AnimationInfo.Add(info);
        await context.SaveChangesAsync(cancellationToken);
        return info.Id;
    }

    public async Task AddRangeAsync(
        IReadOnlyList<FileMapping> mappings,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        await repository.AddRangeAsync(mappings, cancellationToken);
    }

    public async Task<IReadOnlyList<FileMapping>> GetByVirtualPathPrefixAsync(
        string virtualPathPrefix,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        return await repository.GetByVirtualPathPrefixAsync(virtualPathPrefix, cancellationToken);
    }

    public async Task<IReadOnlyList<RootEntry>> GetRootEntriesAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        return await repository.GetRootEntriesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileSystemEntry>> GetImmediateChildrenAsync(
        string parentPath,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        return await repository.GetImmediateChildrenAsync(parentPath, cancellationToken);
    }

    public async Task<FileSystemEntry?> FindFileSystemEntryAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        return await repository.FindFileSystemEntryAsync(path, cancellationToken);
    }

    public async Task<bool> ReplaceAsync(
        Guid animationInfoId,
        IReadOnlyList<FileMapping> mappings,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var current = await context.AnimationInfo
            .AsNoTracking()
            .SingleAsync(info => info.Id == animationInfoId, cancellationToken);
        var repository = new FileMappingRepository(context, _contextOptions);
        return await repository.ReplaceForAnimationInfoAsync(
            animationInfoId,
            current.StateVersion,
            current.FileStore!,
            current.StorePath!,
            mappings,
            cancellationToken);
    }

    public async Task RemoveAsync(Guid animationInfoId, CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new FileMappingRepository(context, _contextOptions);
        await repository.RemoveByAnimationInfoAsync(animationInfoId, cancellationToken);
    }

    public async Task<int> GetHierarchyEntryCountAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.FileSystemEntries.CountAsync(cancellationToken);
    }

    public async Task UpdateVirtualPathDirectlyAsync(
        Guid mappingId,
        string virtualPath,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.FileMappings
            .Where(mapping => mapping.Id == mappingId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(mapping => mapping.VirtualPath, virtualPath),
                cancellationToken);
    }

    public async Task<int> GetMappingCountAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.FileMappings.CountAsync(cancellationToken);
    }

    public async Task<long[]> GetAnimationInfoStateVersionsAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.AnimationInfo
            .OrderBy(info => info.Id)
            .Select(info => info.StateVersion)
            .ToArrayAsync(cancellationToken);
    }

    public async Task SeedCatalogReleaseAsync(
        string? tmdbId,
        DateTimeOffset publishTime,
        int? episode,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        Models.Animation? animation = null;
        if (tmdbId is not null)
        {
            animation = await context.Animations
                .SingleOrDefaultAsync(item => item.TmdbId == tmdbId, cancellationToken);
            if (animation is null)
            {
                animation = new Models.Animation
                {
                    Id = Guid.NewGuid(),
                    TmdbId = tmdbId,
                    Name = "Animation " + tmdbId,
                    OriginalName = "Original " + tmdbId
                };
                context.Animations.Add(animation);
            }
        }

        context.AnimationInfo.Add(new Models.AnimationInfo
        {
            Id = Guid.NewGuid(),
            Title = $"release {tmdbId ?? "uncategorized"} {episode}",
            Description = "summary projection",
            PublishTime = publishTime,
            DownloadType = "torrent",
            DownloadUrl = "https://example.invalid/large.torrent",
            CachedDownloadData = new byte[64 * 1024],
            AdditionalDownloadInfo = "must not be selected",
            Animation = animation,
            Season = 1,
            Episode = episode
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(AnimationCatalogPage Page, string Sql)> GetCatalogPageWithSqlAsync(
        AnimationCatalogCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        var interceptor = new SqlCaptureInterceptor();
        var options = new DbContextOptionsBuilder<Models.ApplicationContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new Models.ApplicationContext(options);
        var repository = new AnimationInfoRepository(context, options);
        var page = await repository.GetAnimationCatalogPageAsync(cursor, take, cancellationToken);
        return (page, string.Join("\n", interceptor.Commands));
    }

    public async Task<AnimationInfoSummaryPage> GetUncategorizedPageAsync(
        AnimationInfoCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new AnimationInfoRepository(context, _contextOptions);
        return await repository.GetUncategorizedPageAsync(cursor, take, cancellationToken);
    }

    public async Task<AnimationEpisodePage?> GetEpisodesPageAsync(
        string tmdbId,
        AnimationInfoCursor? cursor,
        int take,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new AnimationInfoRepository(context, _contextOptions);
        return await repository.GetAnimationEpisodesPageAsync(
            tmdbId,
            cursor,
            take,
            cancellationToken);
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
