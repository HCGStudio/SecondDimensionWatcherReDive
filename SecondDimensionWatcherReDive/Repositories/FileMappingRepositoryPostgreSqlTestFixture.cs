using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

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
        await context.Database.MigrateAsync(cancellationToken);
    }

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

    public async Task<bool> TryAddReleaseAsync(
        string releaseIdentity,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var repository = new AnimationInfoRepository(context, _contextOptions);
        return await repository.TryAddReleaseAsync(new AnimationInfo(
                Guid.NewGuid(),
                "concurrent release",
                string.Empty,
                DateTimeOffset.UtcNow,
                "https://example.test/" + Guid.NewGuid().ToString("N"),
                FileDownloadTypes.HttpDownload,
                [],
                string.Empty,
                false,
                default,
                default,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                false,
                0,
                ReleaseIdentity: releaseIdentity),
            cancellationToken);
    }

    public async Task<int> CountReleaseIdentityAsync(
        string releaseIdentity,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.AnimationInfo.CountAsync(
            info => info.ReleaseIdentity == releaseIdentity,
            cancellationToken);
    }

    public async Task<LibraryScenario> SeedLibraryScenarioAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var now = DateTimeOffset.UtcNow;
        var animation = new Models.Animation
        {
            Id = Guid.NewGuid(),
            TmdbId = "tv-1399",
            Name = "進撃の巨人",
            OriginalName = "Attack on Titan"
        };
        var group = new Models.AnimationGroup { Id = Guid.NewGuid(), Name = "LoliHouse" };
        var current = Release(animation, group, 1, 1, 320, now.AddMinutes(-10),
            FileDownloadTypes.TorrentDownload, true, "torrent:current", 3);
        current.ReleaseResolution = "1080p";
        current.ReleaseCodec = "HEVC";
        current.ReleaseLanguages = ["zh-CN"];
        current.ReleaseScoreReasonsJson = "[\"resolution:1080p:+200\",\"codec:HEVC:+60\"]";
        var duplicate = Release(animation, group, 1, 1, 250, now.AddMinutes(-9),
            FileDownloadTypes.TorrentDownload, true, "torrent:duplicate", 3);
        duplicate.IsActiveRelease = false;
        var imported = Release(animation, group, 1, 2, 520, now.AddMinutes(-8),
            FileDownloadTypes.MediaLibraryImport, true, "import:episode-2", 3);
        imported.ReleaseResolution = "2160p";
        imported.ReleaseCodec = "AV1";
        imported.ReleaseLanguages = ["ja"];
        var upgrade = Release(animation, group, 1, 1, 480, now.AddMinutes(-7),
            FileDownloadTypes.TorrentDownload, false, "torrent:upgrade", 3);
        upgrade.IsActiveRelease = false;
        upgrade.ReleaseResolution = "2160p";
        upgrade.ReleaseCodec = "AV1";
        upgrade.ReleaseScoreReasonsJson = "[\"resolution:2160p:+400\",\"codec:AV1:+80\"]";
        var unidentified = Release(animation, group, 1, null, 100, now.AddMinutes(-6),
            FileDownloadTypes.TorrentDownload, false, "torrent:unknown", 3);
        context.AnimationInfo.AddRange(current, duplicate, imported, upgrade, unidentified);
        context.FileMappings.AddRange(
            MappingEntity(current.Id, "/進撃の巨人/LoliHouse/進撃の巨人 S01E01.mkv"),
            MappingEntity(duplicate.Id, "/進撃の巨人/LoliHouse/進撃の巨人 S01E01 (2).mkv"),
            MappingEntity(imported.Id, "/進撃の巨人/Imported/進撃の巨人 S01E02.mkv"));
        var userId = Guid.NewGuid();
        context.PlaybackProgresses.Add(new Models.PlaybackProgress
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AnimationInfoId = imported.Id,
            VirtualPath = "/進撃の巨人/Imported/進撃の巨人 S01E02.mkv",
            PositionSeconds = 120,
            DurationSeconds = 1200,
            IsWatched = false,
            UpdatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        return new LibraryScenario(userId, current.Id, imported.Id, upgrade.Id);
    }

    public async Task<Guid> InsertConcurrentSearchReleaseAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var info = new Models.AnimationInfo
        {
            Id = Guid.NewGuid(),
            Title = "new concurrent release",
            Description = string.Empty,
            PublishTime = DateTimeOffset.UtcNow.AddHours(1),
            IngestedAt = DateTimeOffset.UtcNow,
            DownloadUrl = "https://example.test/new",
            DownloadType = FileDownloadTypes.TorrentDownload,
            ReleaseIdentity = "torrent:inserted-" + Guid.NewGuid().ToString("N")
        };
        context.AnimationInfo.Add(info);
        await context.SaveChangesAsync(cancellationToken);
        return info.Id;
    }

    public async Task<LibrarySearchResult> SearchAsync(
        LibrarySearchRequest request,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new LibrarySearchRepository(context).SearchAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<LibraryIntegritySummary>> GetIntegrityAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new LibrarySearchRepository(context)
            .GetIntegrityAsync("tv-1399", 1, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetLibraryIndexNamesAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await context.Database.SqlQueryRaw<string>(
                """
                SELECT indexname AS "Value"
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND (indexname LIKE 'IX_%_Trgm'
                       OR indexname = 'IX_AnimationInfo_ReleaseLanguages_Gin'
                       OR indexname = 'UX_AnimationInfo_ReleaseIdentity')
                ORDER BY indexname
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task<UpgradeScenario> SeedUpgradeScenarioAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var animation = new Models.Animation
        {
            Id = Guid.NewGuid(), TmdbId = "upgrade-show", Name = "Upgrade Show", OriginalName = "Upgrade Show"
        };
        var current = Release(animation, null, 1, 1, 200, DateTimeOffset.UtcNow.AddMinutes(-2),
            FileDownloadTypes.TorrentDownload, true, "torrent:old-" + Guid.NewGuid().ToString("N"), 1);
        var candidate = Release(animation, null, 1, 1, 500, DateTimeOffset.UtcNow.AddMinutes(-1),
            FileDownloadTypes.TorrentDownload, true, "torrent:new-" + Guid.NewGuid().ToString("N"), 1);
        candidate.IsActiveRelease = false;
        context.AnimationInfo.AddRange(current, candidate);
        context.FileMappings.AddRange(
            new Models.FileMapping
            {
                Id = Guid.NewGuid(), AnimationInfoId = current.Id,
                VirtualPath = "/Upgrade Show/Old/Upgrade Show S01E01.mkv",
                PhysicalPath = "/store/old.mkv", FileStore = "local"
            },
            new Models.FileMapping
            {
                Id = Guid.NewGuid(), AnimationInfoId = candidate.Id,
                VirtualPath = "/Upgrade Show/New/Upgrade Show S01E01 (2).mkv",
                PhysicalPath = "/store/new.mkv", FileStore = "local"
            });
        await context.SaveChangesAsync(cancellationToken);
        return new UpgradeScenario(new ReleaseUpgradeCandidate(
            current.Id, candidate.Id, animation.Name, 1, 1, 200, 500,
            ["resolution:2160p:+400"], false),
            "/Upgrade Show/Old/Upgrade Show S01E01.mkv");
    }

    public async Task<ReleaseUpgradeOperation?> BeginUpgradeAsync(
        ReleaseUpgradeCandidate candidate,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new ReleaseUpgradeRepository(context, _contextOptions)
            .TryBeginAsync(candidate, DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task<ReleaseUpgradeMutationResult> ActivateUpgradeAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new ReleaseUpgradeRepository(context, _contextOptions)
            .ActivateAsync(operationId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(24), cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetReadyUpgradeCandidateIdsAsync(
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new ReleaseUpgradeRepository(context, _contextOptions)
            .GetReadyCandidateIdsAsync(20, cancellationToken);
    }

    public async Task<ReleaseUpgradeMutationResult> RollbackUpgradeAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new ReleaseUpgradeRepository(context, _contextOptions)
            .RollbackAsync(operationId, DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task<IReadOnlyList<FileMapping>> GetMappingsAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new FileMappingRepository(context, _contextOptions)
            .GetForAnimationInfoAsync(animationInfoId, cancellationToken);
    }

    private static Models.AnimationInfo Release(
        Models.Animation animation,
        Models.AnimationGroup? group,
        int season,
        int? episode,
        int score,
        DateTimeOffset ingestedAt,
        string downloadType,
        bool downloaded,
        string identity,
        int expected) => new()
    {
        Id = Guid.NewGuid(),
        Animation = animation,
        Group = group,
        Title = $"{animation.Name} S{season:D2}E{episode:D2}",
        Description = animation.OriginalName,
        PublishTime = ingestedAt,
        IngestedAt = ingestedAt,
        DownloadUrl = "https://example.test/" + Guid.NewGuid().ToString("N"),
        DownloadType = downloadType,
        IsDownloadTracked = downloaded,
        IsDownloadFinished = downloaded,
        FileStore = downloaded ? "local" : null,
        StorePath = downloaded ? "/store/" + Guid.NewGuid().ToString("N") : null,
        Season = season,
        Episode = episode,
        ReleaseIdentity = identity,
        ReleaseSubtitleGroup = group?.Name,
        ReleaseScore = score,
        ExpectedEpisodeCount = expected,
        IsAiProcessed = true
    };

    private static Models.FileMapping MappingEntity(Guid animationInfoId, string path) => new()
    {
        Id = Guid.NewGuid(),
        AnimationInfoId = animationInfoId,
        VirtualPath = path,
        PhysicalPath = "/store/" + Guid.NewGuid().ToString("N") + ".mkv",
        FileStore = "local"
    };
}

internal sealed record LibraryScenario(
    Guid UserId,
    Guid CurrentReleaseId,
    Guid ImportedReleaseId,
    Guid UpgradeReleaseId);

internal sealed record UpgradeScenario(
    ReleaseUpgradeCandidate Candidate,
    string CanonicalPath);
