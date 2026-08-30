using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    public async Task<(
        Guid ExpectedActiveId,
        IReadOnlyList<Guid> ActiveIds,
        int DowngradedCandidateOperationCount)>
        MigrateDuplicateActiveReleasesAsync(CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(
            "20260829151303_AddLibrarySearchAndReleaseUpgrades",
            cancellationToken);
        var animation = new Models.Animation
        {
            Id = Guid.NewGuid(),
            TmdbId = "migration-active-show",
            Name = "Migration Active Show",
            OriginalName = "Migration Active Show"
        };
        var earlier = Release(
            animation,
            null,
            1,
            1,
            100,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            FileDownloadTypes.TorrentDownload,
            false,
            "migration:earlier-" + Guid.NewGuid().ToString("N"),
            1);
        var later = Release(
            animation,
            null,
            1,
            1,
            200,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            FileDownloadTypes.TorrentDownload,
            false,
            "migration:later-" + Guid.NewGuid().ToString("N"),
            1);
        context.AnimationInfo.AddRange(earlier, later);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        await migrator.MigrateAsync(null, cancellationToken);
        var activeIds = await context.AnimationInfo
            .AsNoTracking()
            .Where(info => info.IsActiveRelease)
            .Select(info => info.Id)
            .ToListAsync(cancellationToken);
        context.ReleaseUpgradeOperations.AddRange(
            new Models.ReleaseUpgradeOperation
            {
                Id = Guid.NewGuid(),
                CurrentReleaseId = earlier.Id,
                CandidateReleaseId = later.Id,
                Status = ReleaseUpgradeStatus.Failed,
                CurrentScore = earlier.ReleaseScore,
                CandidateScore = later.ReleaseScore,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new Models.ReleaseUpgradeOperation
            {
                Id = Guid.NewGuid(),
                CurrentReleaseId = earlier.Id,
                CandidateReleaseId = later.Id,
                Status = ReleaseUpgradeStatus.Failed,
                CurrentScore = earlier.ReleaseScore,
                CandidateScore = later.ReleaseScore,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
        await migrator.MigrateAsync(
            "20260829151303_AddLibrarySearchAndReleaseUpgrades",
            cancellationToken);
        var downgradedCandidateOperationCount = await context.ReleaseUpgradeOperations
            .CountAsync(
                operation => operation.CandidateReleaseId == later.Id,
                cancellationToken);
        await migrator.MigrateAsync(null, cancellationToken);
        return (earlier.Id, activeIds, downgradedCandidateOperationCount);
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
                       OR indexname = 'IX_AnimationInfo_AnimationId'
                       OR indexname IN ('UX_AnimationInfo_ReleaseIdentity',
                                        'UX_AnimationInfo_ActiveEpisodeRelease'))
                ORDER BY indexname
                """)
            .ToListAsync(cancellationToken);
    }

    public async Task<UpgradeScenario> SeedUpgradeScenarioAsync(
        CancellationToken cancellationToken,
        bool includeDuplicateVideoRole = false,
        bool includeCandidateProgress = false)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var animation = new Models.Animation
        {
            Id = Guid.NewGuid(),
            TmdbId = "upgrade-show",
            Name = "Upgrade Show",
            OriginalName = "Upgrade Show"
        };
        var current = Release(animation, null, 1, 1, 200, DateTimeOffset.UtcNow.AddMinutes(-2),
            FileDownloadTypes.TorrentDownload, true, "torrent:old-" + Guid.NewGuid().ToString("N"), 1);
        var candidate = Release(animation, null, 1, 1, 500, DateTimeOffset.UtcNow.AddMinutes(-1),
            FileDownloadTypes.TorrentDownload, true, "torrent:new-" + Guid.NewGuid().ToString("N"), 1);
        candidate.IsActiveRelease = false;
        var canonicalPath = "/Upgrade Show/Old/Upgrade Show S01E01.MKV";
        var canonicalSubtitlePath = "/Upgrade Show/Old/Upgrade Show S01E01.EN.srt";
        context.AnimationInfo.AddRange(current, candidate);
        context.FileMappings.AddRange(
            new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = current.Id,
                VirtualPath = canonicalPath,
                PhysicalPath = "/store/old.mkv",
                FileStore = "local"
            },
            new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = current.Id,
                VirtualPath = canonicalSubtitlePath,
                PhysicalPath = "/store/old.en.srt",
                FileStore = "local"
            },
            new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = candidate.Id,
                VirtualPath = "/Upgrade Show/New/Upgrade Show S01E01 (2).mkv",
                PhysicalPath = "/store/new.mkv",
                FileStore = "local"
            },
            new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = candidate.Id,
                VirtualPath = "/Upgrade Show/New/Upgrade Show S01E01.en (2).srt",
                PhysicalPath = "/store/new.en.srt",
                FileStore = "local"
            });
        if (includeDuplicateVideoRole)
        {
            context.FileMappings.Add(new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = candidate.Id,
                VirtualPath = "/Upgrade Show/New/Upgrade Show S01E01 (3).mkv",
                PhysicalPath = "/store/new-alternate.mkv",
                FileStore = "local"
            });
        }
        var userId = Guid.NewGuid();
        context.PlaybackProgresses.Add(new Models.PlaybackProgress
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AnimationInfoId = current.Id,
            VirtualPath = canonicalPath,
            PositionSeconds = 321,
            DurationSeconds = 1200,
            IsWatched = false,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        if (includeCandidateProgress)
        {
            context.PlaybackProgresses.Add(new Models.PlaybackProgress
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                AnimationInfoId = candidate.Id,
                VirtualPath = "/Upgrade Show/New/Upgrade Show S01E01 (2).mkv",
                PositionSeconds = 1200,
                DurationSeconds = 1200,
                IsWatched = true,
                UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1),
                WatchedAt = DateTimeOffset.UtcNow.AddMinutes(1)
            });
        }
        await context.SaveChangesAsync(cancellationToken);
        return new UpgradeScenario(new ReleaseUpgradeCandidate(
            current.Id, candidate.Id, animation.Name, 1, 1, 200, 500,
            ["resolution:2160p:+400"], false),
            canonicalPath,
            canonicalSubtitlePath,
            userId);
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
        var operation = await context.ReleaseUpgradeOperations
            .AsNoTracking()
            .SingleAsync(item => item.Id == operationId, cancellationToken);
        var repository = new ReleaseUpgradeRepository(context, _contextOptions);
        var activation = await repository.GetActivationAsync(
                             operation.CandidateReleaseId,
                             cancellationToken)
                         ?? throw new InvalidOperationException("Upgrade activation was not found.");
        return await ActivateUpgradeAsync(operationId, activation, cancellationToken);
    }

    public async Task<ReleaseUpgradeActivation?> GetUpgradeActivationAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new ReleaseUpgradeRepository(context, _contextOptions)
            .GetActivationAsync(candidateReleaseId, cancellationToken);
    }

    public async Task<ReleaseUpgradeMutationResult> ActivateUpgradeAsync(
        Guid operationId,
        ReleaseUpgradeActivation expected,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new ReleaseUpgradeRepository(context, _contextOptions)
            .ActivateAsync(
                operationId,
                expected.PreviousMappings,
                expected.CandidateMappings,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(24),
                cancellationToken);
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

    public async Task<ReleaseUpgradeMutationResult> MarkUpgradeFailedAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new ReleaseUpgradeRepository(context, _contextOptions)
            .MarkFailedAsync(operationId, "late failure", cancellationToken);
    }

    public async Task<bool> BeginUpgradeCandidateCancellationAsync(
        Guid animationInfoId,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new AnimationInfoRepository(context, _contextOptions)
            .TryBeginCancelDownloadAsync(
                animationInfoId,
                downloadAttemptId,
                cancellationAttemptId,
                cancellationToken);
    }

    public async Task<bool> FinalizeUpgradeCandidateCancellationAsync(
        Guid animationInfoId,
        Guid? downloadAttemptId,
        Guid cancellationAttemptId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new FileMappingRepository(context, _contextOptions)
            .TryFinalizeDownloadCancellationAsync(
                animationInfoId,
                downloadAttemptId,
                cancellationAttemptId,
                terminalDisposition: null,
                cancellationToken);
    }

    public async Task<ReleaseUpgradeOperation> GetUpgradeOperationAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return (await context.ReleaseUpgradeOperations
                .AsNoTracking()
                .SingleAsync(operation => operation.Id == operationId, cancellationToken))
            .ToRecord();
    }

    public async Task<IReadOnlyList<FileMapping>> GetMappingsAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return await new FileMappingRepository(context, _contextOptions)
            .GetForAnimationInfoAsync(animationInfoId, cancellationToken);
    }

    public async Task<IReadOnlyList<PlaybackProgress>> GetPlaybackProgressesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        return (await context.PlaybackProgresses
                .AsNoTracking()
                .Where(progress => progress.UserId == userId)
                .OrderBy(progress => progress.VirtualPath)
                .ToListAsync(cancellationToken))
            .Select(progress => progress.ToRecord())
            .ToList();
    }

    public async Task ChangeReleaseEpisodeAsync(
        Guid animationInfoId,
        int episode,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.AnimationInfo
            .Where(info => info.Id == animationInfoId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(info => info.Episode, episode),
                cancellationToken);
    }

    public async Task<Guid> SetCandidateDownloadInProgressAsync(
        Guid animationInfoId,
        CancellationToken cancellationToken)
    {
        var downloadAttemptId = Guid.NewGuid();
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.AnimationInfo
            .Where(info => info.Id == animationInfoId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(info => info.IsDownloadTracked, true)
                    .SetProperty(info => info.IsDownloadFinished, false)
                    .SetProperty(info => info.DownloadAttemptId, downloadAttemptId)
                    .SetProperty(info => info.FileStore, (string?)null)
                    .SetProperty(info => info.StorePath, (string?)null),
                cancellationToken);
        return downloadAttemptId;
    }

    public async Task<AnimationInfo?> CancelUpgradeCandidateAsync(
        Guid animationInfoId,
        Guid downloadAttemptId,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        var animationRepository = new AnimationInfoRepository(context, _contextOptions);
        var cancellationAttemptId = Guid.NewGuid();
        if (!await animationRepository.TryBeginCancelDownloadAsync(
                animationInfoId,
                downloadAttemptId,
                cancellationAttemptId,
                cancellationToken))
            return null;
        var mappingRepository = new FileMappingRepository(context, _contextOptions);
        if (!await mappingRepository.TryFinalizeDownloadCancellationAsync(
                animationInfoId,
                downloadAttemptId,
                cancellationAttemptId,
                terminalDisposition: null,
                cancellationToken))
            return null;
        return await animationRepository.FindByIdAsync(animationInfoId, cancellationToken);
    }

    public async Task ChangeMappingPhysicalPathAsync(
        Guid animationInfoId,
        string virtualPath,
        string physicalPath,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await context.FileMappings
            .Where(mapping => mapping.AnimationInfoId == animationInfoId &&
                              mapping.VirtualPath == virtualPath)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(mapping => mapping.PhysicalPath, physicalPath),
                cancellationToken);
    }

    public async Task RemapCandidatePlaybackAsync(
        Guid animationInfoId,
        Guid userId,
        string currentPath,
        string replacementPath,
        CancellationToken cancellationToken)
    {
        await using var context = new Models.ApplicationContext(_contextOptions);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await MappingTransactionLock.AcquireAsync(context, cancellationToken);
        await context.FileMappings
            .Where(mapping => mapping.AnimationInfoId == animationInfoId &&
                              mapping.VirtualPath == currentPath)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(mapping => mapping.VirtualPath, replacementPath),
                cancellationToken);
        await context.PlaybackProgresses
            .Where(progress => progress.AnimationInfoId == animationInfoId &&
                               progress.UserId == userId &&
                               progress.VirtualPath == currentPath)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(progress => progress.VirtualPath, replacementPath),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<(bool FirstActive, bool SecondActive, int FirstEpisode)>
        IdentifyCompetingReleasesAsync(
            CancellationToken cancellationToken,
            bool moveFirst = false,
            bool deidentifyFirst = false,
            bool concurrent = false)
    {
        var animation = new Models.Animation
        {
            Id = Guid.NewGuid(),
            TmdbId = "single-active-show",
            Name = "Single Active Show",
            OriginalName = "Single Active Show"
        };
        var first = Release(
            animation,
            null,
            1,
            null,
            100,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            FileDownloadTypes.TorrentDownload,
            false,
            "torrent:first-" + Guid.NewGuid().ToString("N"),
            12);
        var second = Release(
            animation,
            null,
            1,
            null,
            200,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            FileDownloadTypes.TorrentDownload,
            false,
            "torrent:second-" + Guid.NewGuid().ToString("N"),
            12);
        first.IsActiveRelease = false;
        second.IsActiveRelease = false;
        await using (var seedContext = new Models.ApplicationContext(_contextOptions))
        {
            seedContext.AnimationInfo.AddRange(first, second);
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        if (concurrent)
        {
            async Task IdentifyAsync(Guid releaseId)
            {
                await using var updateContext = new Models.ApplicationContext(_contextOptions);
                var updateRepository = new AnimationInfoRepository(updateContext, _contextOptions);
                var record = await updateRepository.FindByIdAsync(releaseId, cancellationToken)
                             ?? throw new InvalidOperationException("Seeded release could not be reloaded.");
                if (!await updateRepository.TryUpdateAsync(
                        record with
                        {
                            Animation = animation.ToRecord(),
                            Season = 1,
                            Episode = 1
                        },
                        record.StateVersion,
                        cancellationToken))
                    throw new InvalidOperationException("Seeded release could not be identified.");
            }

            await Task.WhenAll(IdentifyAsync(first.Id), IdentifyAsync(second.Id));
        }
        else
        {
            await using var repositoryContext = new Models.ApplicationContext(_contextOptions);
            var repository = new AnimationInfoRepository(repositoryContext, _contextOptions);
            var firstRecord = await repository.FindByIdAsync(first.Id, cancellationToken);
            var secondRecord = await repository.FindByIdAsync(second.Id, cancellationToken);
            if (firstRecord is null || secondRecord is null)
                throw new InvalidOperationException("Seeded releases could not be reloaded.");

            var animationRecord = animation.ToRecord();
            if (!await repository.TryUpdateAsync(
                    firstRecord with { Animation = animationRecord, Season = 1, Episode = 1 },
                    firstRecord.StateVersion,
                    cancellationToken) ||
                !await repository.TryUpdateAsync(
                    secondRecord with { Animation = animationRecord, Season = 1, Episode = 1 },
                    secondRecord.StateVersion,
                    cancellationToken))
                throw new InvalidOperationException("Seeded releases could not be identified.");

            if (moveFirst)
            {
                var identifiedFirst = await repository.FindByIdAsync(first.Id, cancellationToken)
                                      ?? throw new InvalidOperationException(
                                          "Active release could not be reloaded.");
                if (!await repository.TryUpdateAsync(
                        identifiedFirst with { Episode = 2 },
                        identifiedFirst.StateVersion,
                        cancellationToken))
                    throw new InvalidOperationException("Active release could not be moved.");
            }
            else if (deidentifyFirst)
            {
                var identifiedFirst = await repository.FindByIdAsync(first.Id, cancellationToken)
                                      ?? throw new InvalidOperationException(
                                          "Active release could not be reloaded.");
                if (!await repository.TryUpdateAsync(
                        identifiedFirst with { Animation = null },
                        identifiedFirst.StateVersion,
                        cancellationToken))
                    throw new InvalidOperationException("Active release could not be de-identified.");
            }
        }

        await using var readContext = new Models.ApplicationContext(_contextOptions);
        var releases = await readContext.AnimationInfo
            .Where(info => info.Id == first.Id || info.Id == second.Id)
            .ToDictionaryAsync(info => info.Id, cancellationToken);
        return (
            releases[first.Id].IsActiveRelease,
            releases[second.Id].IsActiveRelease,
            releases[first.Id].Episode!.Value);
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
            IsAiProcessed = true,
            IsActiveRelease = true
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
    string CanonicalPath,
    string CanonicalSubtitlePath,
    Guid UserId);
