using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed partial class ReleaseUpgradeRepository(
    Models.ApplicationContext context,
    DbContextOptions<Models.ApplicationContext> contextOptions) : IReleaseUpgradeRepository
{
    private sealed record CandidateRow(
        Guid CurrentReleaseId,
        Guid CandidateReleaseId,
        string AnimationName,
        int? Season,
        int? Episode,
        int CurrentScore,
        int CandidateScore,
        DateTimeOffset CandidatePublishTime,
        string? ReleaseScoreReasonsJson,
        bool Automatic);

    public async Task<IReadOnlyList<ReleaseUpgradeCandidate>> GetCandidatesAsync(
        bool automaticOnly,
        int take,
        CancellationToken cancellationToken)
    {
        if (take is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(take));

        var candidatePairs = BuildCandidatePairs(automaticOnly, DateTimeOffset.UtcNow);

        // Rank only candidates that are actually eligible for this scan. In
        // particular, an ineligible higher-scored release must not hide a lower
        // release whose source policy permits automatic upgrades.
        var rows = await SelectBestCandidatePairs(candidatePairs)
            .OrderByDescending(pair => pair.CandidateScore - pair.CurrentScore)
            .ThenBy(pair => pair.AnimationName)
            .ThenBy(pair => pair.Season)
            .ThenBy(pair => pair.Episode)
            .ThenBy(pair => pair.CandidateReleaseId)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(ToCandidate).ToList();
    }

    public async Task<ReleaseUpgradeCandidate?> FindCandidateAsync(
        Guid currentReleaseId,
        Guid candidateReleaseId,
        CancellationToken cancellationToken)
    {
        var row = await SelectBestCandidatePairs(
                BuildCandidatePairs(automaticOnly: false, DateTimeOffset.UtcNow))
            .SingleOrDefaultAsync(pair =>
                    pair.CurrentReleaseId == currentReleaseId &&
                    pair.CandidateReleaseId == candidateReleaseId,
                cancellationToken);
        return row is null ? null : ToCandidate(row);
    }

    private IQueryable<CandidateRow> BuildCandidatePairs(bool automaticOnly, DateTimeOffset now)
    {
        var eligibleCandidates = context.AnimationInfo.AsNoTracking()
            .Where(candidate =>
                !candidate.IsActiveRelease &&
                candidate.MediaLibraryMissingSince == null &&
                candidate.Season != null &&
                candidate.Episode != null &&
                candidate.DownloadCancellationId == null &&
                !context.ReleaseUpgradeOperations.Any(operation =>
                    operation.CandidateReleaseId == candidate.Id &&
                    (automaticOnly || operation.Status != ReleaseUpgradeStatus.Failed)));
        var candidatePairs =
            from current in context.AnimationInfo.AsNoTracking()
            from candidate in eligibleCandidates
            let automatic = candidate.SourceFeedId != null &&
                            context.SubscriptionAutomationPolicies.Any(policy =>
                                policy.FeedId == candidate.SourceFeedId &&
                                policy.EnableVersionUpgrade &&
                                candidate.ReleaseScore - current.ReleaseScore >=
                                policy.MinimumUpgradeScore)
            where current.IsActiveRelease &&
                  current.IsDownloadFinished &&
                  current.MediaLibraryMissingSince == null &&
                  current.Animation != null &&
                  current.Season != null &&
                  current.Episode != null &&
                  context.FileMappings.Any(mapping => mapping.AnimationInfoId == current.Id) &&
                  !context.ReleaseUpgradeOperations.Any(operation =>
                      operation.CandidateReleaseId == current.Id &&
                      operation.Status == ReleaseUpgradeStatus.Applied &&
                      (operation.RollbackUntil == null || operation.RollbackUntil > now)) &&
                  EF.Property<Guid?>(candidate, "AnimationId") ==
                  EF.Property<Guid?>(current, "AnimationId") &&
                  candidate.Season == current.Season &&
                  candidate.Episode == current.Episode &&
                  candidate.ReleaseScore > current.ReleaseScore &&
                  (!automaticOnly || automatic)
            select new CandidateRow(
                current.Id,
                candidate.Id,
                current.Animation!.Name,
                current.Season,
                current.Episode,
                current.ReleaseScore,
                candidate.ReleaseScore,
                candidate.PublishTime,
                candidate.ReleaseScoreReasonsJson,
                automatic);
        return candidatePairs;
    }

    private static IQueryable<CandidateRow> SelectBestCandidatePairs(
        IQueryable<CandidateRow> candidatePairs)
    {
        var bestCandidateIds = candidatePairs
            .GroupBy(pair => pair.CurrentReleaseId)
            .Select(group => group
                .OrderByDescending(pair => pair.CandidateScore)
                .ThenByDescending(pair => pair.CandidatePublishTime)
                .ThenBy(pair => pair.CandidateReleaseId)
                .Select(pair => pair.CandidateReleaseId)
                .First());
        return candidatePairs.Where(pair => bestCandidateIds.Contains(pair.CandidateReleaseId));
    }

    private static ReleaseUpgradeCandidate ToCandidate(CandidateRow row) => new(
        row.CurrentReleaseId,
        row.CandidateReleaseId,
        row.AnimationName,
        row.Season.GetValueOrDefault(),
        row.Episode.GetValueOrDefault(),
        row.CurrentScore,
        row.CandidateScore,
        ParseReasons(row.ReleaseScoreReasonsJson),
        row.Automatic);

    public async Task<ReleaseUpgradeOperation?> TryBeginAsync(
        ReleaseUpgradeCandidate candidate,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var releases = await MappingTransactionLock.LockAnimationInfosAsync(
                writeContext,
                [candidate.CurrentReleaseId, candidate.CandidateReleaseId],
                cancellationToken);
            if (!releases.TryGetValue(candidate.CurrentReleaseId, out var current) ||
                !releases.TryGetValue(candidate.CandidateReleaseId, out var next))
                return null;
            await writeContext.Entry(current).Reference(info => info.Animation).LoadAsync(cancellationToken);
            await writeContext.Entry(next).Reference(info => info.Animation).LoadAsync(cancellationToken);
            if (current.Animation is null || next.Animation is null ||
                current.Animation.Id != next.Animation.Id ||
                current.Season != next.Season ||
                current.Episode != next.Episode ||
                next.ReleaseScore <= current.ReleaseScore ||
                !current.IsActiveRelease ||
                next.IsActiveRelease ||
                (next.IsDownloadTracked && next.DownloadCancellationId is not null) ||
                !current.IsDownloadFinished ||
                !await writeContext.FileMappings.AnyAsync(
                    mapping => mapping.AnimationInfoId == current.Id,
                    cancellationToken))
                return null;

            await writeContext.ReleaseUpgradeOperations
                .Where(operation => operation.Status == ReleaseUpgradeStatus.Applied &&
                                    operation.RollbackUntil <= createdAt)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(operation => operation.Status, ReleaseUpgradeStatus.Completed)
                        .SetProperty(operation => operation.CompletedAt, createdAt),
                    cancellationToken);
            if (await writeContext.ReleaseUpgradeOperations.AnyAsync(
                    operation => operation.CandidateReleaseId == current.Id &&
                                 operation.Status == ReleaseUpgradeStatus.Applied,
                    cancellationToken))
                return null;

            if (await writeContext.ReleaseUpgradeOperations.AnyAsync(
                    operation => (operation.CandidateReleaseId == next.Id &&
                                  operation.Status != ReleaseUpgradeStatus.Failed) ||
                                 (operation.CurrentReleaseId == current.Id &&
                                  (operation.Status == ReleaseUpgradeStatus.Downloading ||
                                   operation.Status == ReleaseUpgradeStatus.Verifying ||
                                   operation.Status == ReleaseUpgradeStatus.Applied)),
                    cancellationToken))
                return null;

            var entity = new Models.ReleaseUpgradeOperation
            {
                Id = Guid.NewGuid(),
                CurrentReleaseId = current.Id,
                CandidateReleaseId = next.Id,
                Status = next.IsDownloadFinished
                    ? ReleaseUpgradeStatus.Verifying
                    : ReleaseUpgradeStatus.Downloading,
                CurrentScore = current.ReleaseScore,
                CandidateScore = next.ReleaseScore,
                CreatedAt = createdAt
            };
            writeContext.ReleaseUpgradeOperations.Add(entity);
            try
            {
                await writeContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return entity.ToRecord();
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return null;
            }
        });
    }

    public async Task<ReleaseUpgradeOperation?> FindActiveByCandidateAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken)
    {
        var operation = await context.ReleaseUpgradeOperations.AsNoTracking()
            .FirstOrDefaultAsync(item => item.CandidateReleaseId == candidateReleaseId &&
                                         (item.Status == ReleaseUpgradeStatus.Downloading ||
                                          item.Status == ReleaseUpgradeStatus.Verifying),
                cancellationToken);
        return operation?.ToRecord();
    }

    public async Task<IReadOnlyList<Guid>> GetReadyCandidateIdsAsync(
        int take,
        CancellationToken cancellationToken)
    {
        if (take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        return await context.ReleaseUpgradeOperations.AsNoTracking()
            .Where(operation =>
                (operation.Status == ReleaseUpgradeStatus.Downloading ||
                 operation.Status == ReleaseUpgradeStatus.Verifying) &&
                operation.CandidateRelease.IsDownloadFinished &&
                context.FileMappings.Any(mapping =>
                    mapping.AnimationInfoId == operation.CandidateReleaseId))
            .OrderBy(operation => operation.CreatedAt)
            .Select(operation => operation.CandidateReleaseId)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<ReleaseUpgradeActivation?> GetActivationAsync(
        Guid candidateReleaseId,
        CancellationToken cancellationToken)
    {
        var operation = await context.ReleaseUpgradeOperations.AsNoTracking()
            .FirstOrDefaultAsync(item => item.CandidateReleaseId == candidateReleaseId &&
                                         (item.Status == ReleaseUpgradeStatus.Downloading ||
                                          item.Status == ReleaseUpgradeStatus.Verifying),
                cancellationToken);
        if (operation is null) return null;

        var mappings = await context.FileMappings.AsNoTracking()
            .Where(mapping => mapping.AnimationInfoId == operation.CurrentReleaseId ||
                              mapping.AnimationInfoId == operation.CandidateReleaseId)
            .OrderBy(mapping => mapping.VirtualPath)
            .ToListAsync(cancellationToken);
        return new ReleaseUpgradeActivation(
            operation.ToRecord(),
            mappings.Where(mapping => mapping.AnimationInfoId == operation.CurrentReleaseId)
                .Select(mapping => mapping.ToRecord()).ToList(),
            mappings.Where(mapping => mapping.AnimationInfoId == operation.CandidateReleaseId)
                .Select(mapping => mapping.ToRecord()).ToList());
    }

    public async Task<ReleaseUpgradeActivation?> GetRollbackAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var operation = await context.ReleaseUpgradeOperations.AsNoTracking()
            .Include(item => item.MappingSnapshots)
            .SingleOrDefaultAsync(item => item.Id == operationId &&
                                          item.Status == ReleaseUpgradeStatus.Applied,
                cancellationToken);
        if (operation is null) return null;

        var candidateMappings = await context.FileMappings.AsNoTracking()
            .Where(mapping => mapping.AnimationInfoId == operation.CandidateReleaseId)
            .OrderBy(mapping => mapping.VirtualPath)
            .Select(mapping => mapping.ToRecord())
            .ToListAsync(cancellationToken);
        var previousMappings = operation.MappingSnapshots
            .Where(snapshot => snapshot.Kind == ReleaseUpgradeMappingKind.Previous)
            .OrderBy(snapshot => snapshot.VirtualPath, StringComparer.Ordinal)
            .Select(snapshot => FromSnapshot(snapshot).ToRecord())
            .ToList();
        return new ReleaseUpgradeActivation(
            operation.ToRecord(),
            previousMappings,
            candidateMappings);
    }

    public async Task<ReleaseUpgradeMutationResult> ActivateAsync(
        Guid operationId,
        IReadOnlyList<FileMapping> expectedPreviousMappings,
        IReadOnlyList<FileMapping> expectedCandidateMappings,
        DateTimeOffset verifiedAt,
        DateTimeOffset rollbackUntil,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var operation = await writeContext.ReleaseUpgradeOperations
                .Include(item => item.MappingSnapshots)
                .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
            if (operation is null)
                return new ReleaseUpgradeMutationResult(false, "not_found", null);
            if (operation.Status == ReleaseUpgradeStatus.Applied)
                return new ReleaseUpgradeMutationResult(true, "already_applied", operation.ToRecord());
            if (operation.Status is not (ReleaseUpgradeStatus.Downloading or ReleaseUpgradeStatus.Verifying))
                return new ReleaseUpgradeMutationResult(false, "invalid_state", operation.ToRecord());

            var infos = await MappingTransactionLock.LockAnimationInfosAsync(
                writeContext,
                [operation.CurrentReleaseId, operation.CandidateReleaseId],
                cancellationToken);
            if (!infos.TryGetValue(operation.CurrentReleaseId, out var current) ||
                !infos.TryGetValue(operation.CandidateReleaseId, out var candidate) ||
                !candidate.IsDownloadFinished)
                return new ReleaseUpgradeMutationResult(false, "candidate_not_ready", operation.ToRecord());
            if (current.DownloadCancellationId is not null ||
                candidate.DownloadCancellationId is not null)
                return new ReleaseUpgradeMutationResult(false, "download_cancelling", operation.ToRecord());
            if (!AreSameEpisode(writeContext, current, candidate) ||
                !current.IsActiveRelease ||
                candidate.IsActiveRelease ||
                candidate.ReleaseScore <= current.ReleaseScore)
                return new ReleaseUpgradeMutationResult(false, "release_changed", operation.ToRecord());

            var mappings = await writeContext.FileMappings
                .Where(mapping => mapping.AnimationInfoId == current.Id ||
                                  mapping.AnimationInfoId == candidate.Id)
                .OrderBy(mapping => mapping.VirtualPath)
                .ToListAsync(cancellationToken);
            var previous = mappings.Where(mapping => mapping.AnimationInfoId == current.Id).ToList();
            var next = mappings.Where(mapping => mapping.AnimationInfoId == candidate.Id).ToList();
            if (previous.Count == 0 || next.Count == 0)
                return new ReleaseUpgradeMutationResult(false, "mapping_missing", operation.ToRecord());
            if (!MappingSetsMatch(previous, expectedPreviousMappings) ||
                !MappingSetsMatch(next, expectedCandidateMappings))
                return new ReleaseUpgradeMutationResult(false, "mapping_changed", operation.ToRecord());

            var snapshots = previous
                .Select(mapping => ToSnapshot(
                    operation.Id,
                    mapping,
                    ReleaseUpgradeMappingKind.Previous))
                .Concat(next.Select(mapping => ToSnapshot(
                    operation.Id,
                    mapping,
                    ReleaseUpgradeMappingKind.Candidate)))
                .ToList();
            await writeContext.ReleaseUpgradeMappingSnapshots.AddRangeAsync(
                snapshots,
                cancellationToken);

            var replacement = BuildCandidateReplacement(previous, next, candidate.Id);
            var reconciliation = await FileMappingSetReconciler.ReconcileAcrossOwnersAsync(
                writeContext,
                [current.Id, candidate.Id],
                replacement.Mappings,
                cancellationToken);
            await TransferPlaybackProgressAsync(
                writeContext,
                BuildActivationPlaybackTransfers(current.Id, candidate.Id, replacement),
                cancellationToken);
            await writeContext.AnimationInfo
                .Where(info => info.Id == current.Id)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(info => info.StateVersion, info => info.StateVersion + 1)
                        .SetProperty(info => info.IsActiveRelease, false),
                    cancellationToken);
            await writeContext.AnimationInfo
                .Where(info => info.Id == candidate.Id)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(info => info.StateVersion, info => info.StateVersion + 1)
                        .SetProperty(info => info.IsActiveRelease, true),
                    cancellationToken);
            operation.Status = ReleaseUpgradeStatus.Applied;
            operation.VerifiedAt = verifiedAt;
            operation.AppliedAt = verifiedAt;
            operation.RollbackUntil = rollbackUntil;
            await writeContext.SaveChangesAsync(cancellationToken);
            await reconciliation.RestoreEntryIdentitiesAsync(writeContext, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ReleaseUpgradeMutationResult(true, "applied", operation.ToRecord());
        });
    }

    public async Task<ReleaseUpgradeMutationResult> MarkFailedAsync(
        Guid operationId,
        string failureSummary,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            await writeContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"ReleaseUpgradeOperations\" WHERE \"Id\" = {operationId} FOR UPDATE",
                cancellationToken);
            var operation = await writeContext.ReleaseUpgradeOperations
                .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
            if (operation is null)
                return new ReleaseUpgradeMutationResult(false, "not_found", null);
            if (operation.Status == ReleaseUpgradeStatus.Failed)
                return new ReleaseUpgradeMutationResult(true, "already_failed", operation.ToRecord());
            if (operation.Status is not (ReleaseUpgradeStatus.Downloading or ReleaseUpgradeStatus.Verifying))
                return new ReleaseUpgradeMutationResult(false, "invalid_state", operation.ToRecord());

            operation.Status = ReleaseUpgradeStatus.Failed;
            operation.FailureSummary = failureSummary.Length <= 2048
                ? failureSummary
                : failureSummary[..2048];
            operation.CompletedAt = DateTimeOffset.UtcNow;
            await writeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ReleaseUpgradeMutationResult(true, "failed", operation.ToRecord());
        });
    }

    public async Task<ReleaseUpgradeMutationResult> RollbackAsync(
        Guid operationId,
        DateTimeOffset rolledBackAt,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(contextOptions);
            await using var transaction = await writeContext.Database.BeginTransactionAsync(cancellationToken);
            await MappingTransactionLock.AcquireAsync(writeContext, cancellationToken);
            var operation = await writeContext.ReleaseUpgradeOperations
                .Include(item => item.MappingSnapshots)
                .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
            if (operation is null)
                return new ReleaseUpgradeMutationResult(false, "not_found", null);
            if (operation.Status == ReleaseUpgradeStatus.RolledBack)
                return new ReleaseUpgradeMutationResult(true, "already_rolled_back", operation.ToRecord());
            if (operation.Status != ReleaseUpgradeStatus.Applied || operation.RollbackUntil < rolledBackAt)
                return new ReleaseUpgradeMutationResult(false, "rollback_unavailable", operation.ToRecord());

            var infos = await MappingTransactionLock.LockAnimationInfosAsync(
                writeContext,
                [operation.CurrentReleaseId, operation.CandidateReleaseId],
                cancellationToken);
            if (!infos.TryGetValue(operation.CurrentReleaseId, out var current) ||
                !infos.TryGetValue(operation.CandidateReleaseId, out var activeCandidate) ||
                !AreSameEpisode(writeContext, current, activeCandidate) ||
                current.IsActiveRelease ||
                !activeCandidate.IsActiveRelease)
                return new ReleaseUpgradeMutationResult(false, "release_changed", operation.ToRecord());
            var previous = operation.MappingSnapshots
                .Where(snapshot => snapshot.Kind == ReleaseUpgradeMappingKind.Previous)
                .ToList();
            var candidate = operation.MappingSnapshots
                .Where(snapshot => snapshot.Kind == ReleaseUpgradeMappingKind.Candidate)
                .ToList();
            if (previous.Count == 0 || candidate.Count == 0)
                return new ReleaseUpgradeMutationResult(false, "snapshot_missing", operation.ToRecord());

            var replacement = BuildCandidateReplacement(
                previous.Select(FromSnapshot).ToList(),
                candidate.Select(FromSnapshot).ToList(),
                operation.CandidateReleaseId);
            var currentCandidateMappings = await writeContext.FileMappings
                .AsNoTracking()
                .Where(mapping => mapping.AnimationInfoId == operation.CandidateReleaseId)
                .ToListAsync(cancellationToken);
            if (!MappingSetsMatch(currentCandidateMappings, replacement.Mappings))
                return new ReleaseUpgradeMutationResult(false, "mapping_changed", operation.ToRecord());
            await TransferPlaybackProgressAsync(
                writeContext,
                BuildRollbackPlaybackTransfers(
                    operation.CurrentReleaseId,
                    operation.CandidateReleaseId,
                    replacement),
                cancellationToken);

            var desiredPreviousMappings = previous.Select(snapshot => new Models.FileMapping
            {
                Id = snapshot.OriginalMappingId,
                AnimationInfoId = snapshot.AnimationInfoId,
                VirtualPath = snapshot.VirtualPath,
                PhysicalPath = snapshot.PhysicalPath,
                FileStore = snapshot.FileStore
            }).ToList();
            var reconciliation = await FileMappingSetReconciler.ReconcileAcrossOwnersAsync(
                writeContext,
                [operation.CurrentReleaseId, operation.CandidateReleaseId],
                desiredPreviousMappings,
                cancellationToken);
            await writeContext.AnimationInfo
                .Where(info => info.Id == operation.CandidateReleaseId)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(info => info.StateVersion, info => info.StateVersion + 1)
                        .SetProperty(info => info.IsActiveRelease, false),
                    cancellationToken);
            await writeContext.AnimationInfo
                .Where(info => info.Id == operation.CurrentReleaseId)
                .ExecuteUpdateAsync(setters => setters
                        .SetProperty(info => info.StateVersion, info => info.StateVersion + 1)
                        .SetProperty(info => info.IsActiveRelease, true),
                    cancellationToken);
            operation.Status = ReleaseUpgradeStatus.RolledBack;
            operation.CompletedAt = rolledBackAt;
            await writeContext.SaveChangesAsync(cancellationToken);
            await reconciliation.RestoreEntryIdentitiesAsync(writeContext, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ReleaseUpgradeMutationResult(true, "rolled_back", operation.ToRecord());
        });
    }

    public async Task<IReadOnlyList<ReleaseUpgradeOperation>> GetHistoryAsync(
        int take,
        CancellationToken cancellationToken)
    {
        if (take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(take));
        return (await context.ReleaseUpgradeOperations.AsNoTracking()
                .OrderByDescending(operation => operation.CreatedAt)
                .Take(take)
                .ToListAsync(cancellationToken))
            .Select(operation => operation.ToRecord())
            .ToList();
    }

    public Task<int> CompleteExpiredAsync(
        DateTimeOffset completedAt,
        CancellationToken cancellationToken) =>
        context.ReleaseUpgradeOperations
            .Where(operation => operation.Status == ReleaseUpgradeStatus.Applied &&
                                operation.RollbackUntil <= completedAt)
            .ExecuteUpdateAsync(setters => setters
                    .SetProperty(operation => operation.Status, ReleaseUpgradeStatus.Completed)
                    .SetProperty(operation => operation.CompletedAt, completedAt),
                cancellationToken);

    private static Models.ReleaseUpgradeMappingSnapshot ToSnapshot(
        Guid operationId,
        Models.FileMapping mapping,
        ReleaseUpgradeMappingKind kind) => new()
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            Kind = kind,
            OriginalMappingId = mapping.Id,
            AnimationInfoId = mapping.AnimationInfoId,
            VirtualPath = mapping.VirtualPath,
            PhysicalPath = mapping.PhysicalPath,
            FileStore = mapping.FileStore
        };

    private static CandidateReplacementPlan BuildCandidateReplacement(
        IReadOnlyList<Models.FileMapping> previous,
        IReadOnlyList<Models.FileMapping> candidate,
        Guid candidateReleaseId)
    {
        var previousByRole = previous
            .GroupBy(mapping => GetStableFileRole(mapping.VirtualPath), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);
        var matchedPreviousIds = new HashSet<Guid>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Models.FileMapping>(candidate.Count);
        var candidatePathReplacements = new Dictionary<string, string>(StringComparer.Ordinal);
        var previousPathReplacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var mapping in candidate
                     .OrderBy(item => item.VirtualPath, StringComparer.Ordinal)
                     .ThenBy(item => item.PhysicalPath, StringComparer.Ordinal)
                     .ThenBy(item => item.Id))
        {
            var role = GetStableFileRole(mapping.VirtualPath);
            var matchedPrevious = previousByRole.GetValueOrDefault(role);
            var usesPreviousPath = matchedPrevious is not null &&
                                   matchedPreviousIds.Add(matchedPrevious.Id);
            var virtualPath = usesPreviousPath
                ? matchedPrevious!.VirtualPath
                : mapping.VirtualPath;
            if (!used.Add(virtualPath))
                throw new InvalidOperationException(
                    $"Candidate replacement produced duplicate virtual path '{virtualPath}'.");

            candidatePathReplacements.Add(mapping.VirtualPath, virtualPath);
            if (usesPreviousPath)
                previousPathReplacements.TryAdd(matchedPrevious!.VirtualPath, virtualPath);
            result.Add(new Models.FileMapping
            {
                Id = Guid.NewGuid(),
                AnimationInfoId = candidateReleaseId,
                VirtualPath = virtualPath,
                PhysicalPath = mapping.PhysicalPath,
                FileStore = mapping.FileStore
            });
        }

        return new CandidateReplacementPlan(
            result,
            candidatePathReplacements,
            previousPathReplacements);
    }

    private static string GetStableFileRole(string virtualPath)
    {
        var fileName = virtualPath[(virtualPath.LastIndexOf('/') + 1)..];
        var extension = Path.GetExtension(fileName);
        var stem = extension.Length == 0 ? fileName : fileName[..^extension.Length];
        return CollisionSuffixRegex().Replace(stem, string.Empty) + extension;
    }

    private static Models.FileMapping FromSnapshot(Models.ReleaseUpgradeMappingSnapshot snapshot) => new()
    {
        Id = snapshot.OriginalMappingId,
        AnimationInfoId = snapshot.AnimationInfoId,
        VirtualPath = snapshot.VirtualPath,
        PhysicalPath = snapshot.PhysicalPath,
        FileStore = snapshot.FileStore
    };

    private static bool AreSameEpisode(
        Models.ApplicationContext writeContext,
        Models.AnimationInfo current,
        Models.AnimationInfo candidate)
    {
        var currentAnimationId = writeContext.Entry(current)
            .Property<Guid?>("AnimationId")
            .CurrentValue;
        var candidateAnimationId = writeContext.Entry(candidate)
            .Property<Guid?>("AnimationId")
            .CurrentValue;
        return currentAnimationId is not null &&
               currentAnimationId == candidateAnimationId &&
               current.Season is not null &&
               current.Season == candidate.Season &&
               current.Episode is not null &&
               current.Episode == candidate.Episode;
    }

    private static bool MappingSetsMatch(
        IReadOnlyCollection<Models.FileMapping> actual,
        IReadOnlyCollection<Models.FileMapping> expected)
    {
        if (actual.Count != expected.Count) return false;
        var expectedMappings = expected
            .Select(mapping => (mapping.VirtualPath, mapping.PhysicalPath, mapping.FileStore))
            .ToHashSet();
        return actual.All(mapping => expectedMappings.Contains(
            (mapping.VirtualPath, mapping.PhysicalPath, mapping.FileStore)));
    }

    private static bool MappingSetsMatch(
        IReadOnlyCollection<Models.FileMapping> actual,
        IReadOnlyCollection<FileMapping> expected)
    {
        if (actual.Count != expected.Count) return false;
        var expectedMappings = expected
            .Select(mapping => (mapping.VirtualPath, mapping.PhysicalPath, mapping.FileStore))
            .ToHashSet();
        return actual.All(mapping => expectedMappings.Contains(
            (mapping.VirtualPath, mapping.PhysicalPath, mapping.FileStore)));
    }

    private static IReadOnlyDictionary<PlaybackLocation, PlaybackLocation>
        BuildActivationPlaybackTransfers(
            Guid currentReleaseId,
            Guid candidateReleaseId,
            CandidateReplacementPlan replacement)
    {
        var transfers = replacement.CandidatePathReplacements.ToDictionary(
            pair => new PlaybackLocation(candidateReleaseId, pair.Key),
            pair => new PlaybackLocation(candidateReleaseId, pair.Value));
        foreach (var pair in replacement.PreviousPathReplacements)
        {
            transfers[new PlaybackLocation(currentReleaseId, pair.Key)] =
                new PlaybackLocation(candidateReleaseId, pair.Value);
        }

        return transfers;
    }

    private static IReadOnlyDictionary<PlaybackLocation, PlaybackLocation>
        BuildRollbackPlaybackTransfers(
            Guid currentReleaseId,
            Guid candidateReleaseId,
            CandidateReplacementPlan replacement)
    {
        var transfers = new Dictionary<PlaybackLocation, PlaybackLocation>();
        foreach (var pair in replacement.PreviousPathReplacements)
        {
            transfers[new PlaybackLocation(candidateReleaseId, pair.Value)] =
                new PlaybackLocation(currentReleaseId, pair.Key);
        }

        return transfers;
    }

    private static async Task TransferPlaybackProgressAsync(
        Models.ApplicationContext writeContext,
        IReadOnlyDictionary<PlaybackLocation, PlaybackLocation> transfers,
        CancellationToken cancellationToken)
    {
        if (transfers.Count == 0) return;

        var ownerIds = transfers.Keys
            .Select(location => location.AnimationInfoId)
            .Concat(transfers.Values.Select(location => location.AnimationInfoId))
            .Distinct()
            .ToArray();
        var rows = await writeContext.PlaybackProgresses
            .AsNoTracking()
            .Where(progress => ownerIds.Contains(progress.AnimationInfoId))
            .ToListAsync(cancellationToken);
        var targets = transfers.Values.ToHashSet();
        var affected = rows
            .Where(progress =>
            {
                var location = new PlaybackLocation(
                    progress.AnimationInfoId,
                    progress.VirtualPath);
                return transfers.ContainsKey(location) || targets.Contains(location);
            })
            .ToList();
        if (affected.Count == 0) return;

        var affectedIds = affected.Select(progress => progress.Id).ToArray();
        await writeContext.PlaybackProgresses
            .Where(progress => affectedIds.Contains(progress.Id))
            .ExecuteDeleteAsync(cancellationToken);

        var merged = affected
            .Select(progress =>
            {
                var source = new PlaybackLocation(
                    progress.AnimationInfoId,
                    progress.VirtualPath);
                var target = transfers.GetValueOrDefault(source, source);
                return (Progress: progress, Target: target);
            })
            .GroupBy(item => new
            {
                item.Progress.UserId,
                item.Target.AnimationInfoId,
                item.Target.VirtualPath
            })
            .Select(group =>
            {
                // If both releases have progress for the same user/file role,
                // retain the last user action rather than reviving stale state.
                var winner = group
                    .OrderByDescending(item => item.Progress.UpdatedAt)
                    .ThenByDescending(item => item.Progress.Id)
                    .First()
                    .Progress;
                return new Models.PlaybackProgress
                {
                    Id = winner.Id,
                    UserId = group.Key.UserId,
                    AnimationInfoId = group.Key.AnimationInfoId,
                    VirtualPath = group.Key.VirtualPath,
                    PositionSeconds = winner.PositionSeconds,
                    DurationSeconds = winner.DurationSeconds,
                    IsWatched = winner.IsWatched,
                    UpdatedAt = winner.UpdatedAt,
                    WatchedAt = winner.WatchedAt
                };
            })
            .ToList();
        await writeContext.PlaybackProgresses.AddRangeAsync(merged, cancellationToken);
    }

    [GeneratedRegex(@" \(\d+\)$", RegexOptions.CultureInvariant)]
    private static partial Regex CollisionSuffixRegex();

    private readonly record struct PlaybackLocation(Guid AnimationInfoId, string VirtualPath);

    private sealed record CandidateReplacementPlan(
        IReadOnlyList<Models.FileMapping> Mappings,
        IReadOnlyDictionary<string, string> CandidatePathReplacements,
        IReadOnlyDictionary<string, string> PreviousPathReplacements);

    private static IReadOnlyList<string> ParseReasons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}

internal static class ReleaseUpgradeRepositoryConverters
{
    public static ReleaseUpgradeOperation ToRecord(this Models.ReleaseUpgradeOperation operation) =>
        new(operation.Id,
            operation.CurrentReleaseId,
            operation.CandidateReleaseId,
            operation.Status,
            operation.CurrentScore,
            operation.CandidateScore,
            operation.CreatedAt,
            operation.VerifiedAt,
            operation.AppliedAt,
            operation.RollbackUntil,
            operation.CompletedAt,
            operation.FailureSummary);
}
