using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileDownload;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class LibrarySearchRepository(Models.ApplicationContext context)
    : ILibrarySearchRepository
{
    private const int MaximumReturnedPathsPerRelease = 20;

    private sealed record SearchCursor(
        DateTimeOffset SnapshotUtc,
        string Signature,
        long Revision,
        long? WatchRevision,
        Guid LastId,
        DateTimeOffset? PublishedAt,
        int? Score,
        string? SortTitle,
        int? Season,
        int? Episode);

    private sealed record SearchRow(
        Guid Id,
        string Title,
        string SortTitle,
        string? AnimationName,
        string? AnimationOriginalName,
        string? TmdbId,
        int? Season,
        int? Episode,
        string? ReleaseSubtitleGroup,
        string? GroupName,
        string? ReleaseResolution,
        string? ReleaseCodec,
        string[] ReleaseLanguages,
        bool IsDownloadTracked,
        bool IsDownloadFinished,
        string DownloadType,
        int ReleaseScore,
        string? ReleaseScoreReasonsJson,
        DateTimeOffset PublishTime);

    private sealed record IntegrityRelease(
        Guid Id,
        string TmdbId,
        string AnimationName,
        int? Season,
        int? Episode,
        int? ExpectedEpisodeCount,
        bool IsDownloadFinished,
        bool IsActiveRelease,
        Guid? DownloadCancellationId,
        int ReleaseScore,
        DateTimeOffset PublishTime,
        Guid? SourceFeedId,
        string? ReleaseScoreReasonsJson);

    private sealed class MappingPathRow
    {
        public Guid AnimationInfoId { get; init; }
        public string VirtualPath { get; init; } = string.Empty;
        public long TotalCount { get; init; }
    }

    public async Task<LibrarySearchResult> SearchAsync(
        LibrarySearchRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Take is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request), "Take must be between 1 and 100.");

        var signature = Signature(request);
        var cursor = DecodeCursor(request.Cursor);
        if (cursor is not null && !string.Equals(cursor.Signature, signature, StringComparison.Ordinal))
            throw new ArgumentException("The search cursor does not match the active filters.", nameof(request));
        var revision = await ReadLibraryRevisionAsync(cancellationToken);
        if (cursor is not null && cursor.Revision != revision)
            throw new ArgumentException("The library changed; restart search pagination.", nameof(request));
        var watchRevision = await ReadWatchRevisionAsync(
            request.UserId,
            request.WatchState,
            cancellationToken);
        if (cursor is not null && cursor.WatchRevision != watchRevision)
            throw new ArgumentException("The playback state changed; restart search pagination.", nameof(request));
        var snapshot = cursor?.SnapshotUtc ?? DateTimeOffset.UtcNow;
        if (cursor is not null && cursor.LastId == Guid.Empty)
            throw new ArgumentException("The search cursor is invalid.", nameof(request));

        var query = context.AnimationInfo
            .AsNoTracking()
            .Where(info => info.MediaLibraryMissingSince == null && info.IngestedAt <= snapshot);

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var pattern = ContainsPattern(request.Query);
            query = query.Where(info =>
                EF.Functions.ILike(info.Title, pattern, "\\") ||
                EF.Functions.ILike(info.Description, pattern, "\\") ||
                (info.Animation != null &&
                 (EF.Functions.ILike(info.Animation.Name, pattern, "\\") ||
                  EF.Functions.ILike(info.Animation.OriginalName, pattern, "\\") ||
                  EF.Functions.ILike(info.Animation.TmdbId, pattern, "\\"))) ||
                (info.Group != null && EF.Functions.ILike(info.Group.Name, pattern, "\\")) ||
                context.FileMappings.Any(mapping =>
                    mapping.AnimationInfoId == info.Id &&
                    EF.Functions.ILike(mapping.VirtualPath, pattern, "\\")));
        }

        if (request.Season is { } season) query = query.Where(info => info.Season == season);
        if (request.Episode is { } episode) query = query.Where(info => info.Episode == episode);
        if (!string.IsNullOrWhiteSpace(request.SubtitleGroup))
            query = query.Where(info =>
                info.ReleaseSubtitleGroup == request.SubtitleGroup ||
                (info.ReleaseSubtitleGroup == null && info.Group != null &&
                 info.Group.Name == request.SubtitleGroup));
        if (!string.IsNullOrWhiteSpace(request.Resolution))
            query = query.Where(info => info.ReleaseResolution == request.Resolution);
        if (!string.IsNullOrWhiteSpace(request.Codec))
            query = query.Where(info => info.ReleaseCodec == request.Codec);
        if (!string.IsNullOrWhiteSpace(request.Language))
            query = query.Where(info => info.ReleaseLanguages.Contains(request.Language));
        if (!string.IsNullOrWhiteSpace(request.VirtualPath))
        {
            var pathPattern = ContainsPattern(request.VirtualPath);
            query = query.Where(info => context.FileMappings.Any(mapping =>
                mapping.AnimationInfoId == info.Id &&
                EF.Functions.ILike(mapping.VirtualPath, pathPattern, "\\")));
        }

        query = request.DownloadState switch
        {
            LibraryDownloadState.NotDownloaded => query.Where(info => !info.IsDownloadTracked),
            LibraryDownloadState.Downloading => query.Where(info =>
                info.IsDownloadTracked && !info.IsDownloadFinished),
            LibraryDownloadState.Downloaded => query.Where(info => info.IsDownloadFinished),
            _ => query
        };
        query = request.Source switch
        {
            LibrarySourceKind.Torrent => query.Where(info =>
                info.DownloadType == FileDownloadTypes.TorrentDownload),
            LibrarySourceKind.MediaLibraryImport => query.Where(info =>
                info.DownloadType == FileDownloadTypes.MediaLibraryImport),
            _ => query
        };
        query = request.WatchState switch
        {
            LibraryWatchState.Watched => query.Where(info => context.PlaybackProgresses.Any(progress =>
                progress.UserId == request.UserId && progress.AnimationInfoId == info.Id && progress.IsWatched)),
            LibraryWatchState.InProgress => query.Where(info => context.PlaybackProgresses.Any(progress =>
                progress.UserId == request.UserId && progress.AnimationInfoId == info.Id &&
                !progress.IsWatched && progress.PositionSeconds > 0)),
            LibraryWatchState.Unwatched => query.Where(info => !context.PlaybackProgresses.Any(progress =>
                progress.UserId == request.UserId && progress.AnimationInfoId == info.Id &&
                (progress.IsWatched || progress.PositionSeconds > 0))),
            _ => query
        };

        if (cursor is not null)
            query = SeekAfter(query, request.Sort, cursor);

        var ordered = request.Sort switch
        {
            LibrarySearchSort.TitleAscending => query
                .OrderBy(info => info.Animation == null ? info.Title : info.Animation.Name)
                .ThenBy(info => info.Season ?? int.MaxValue)
                .ThenBy(info => info.Episode ?? int.MaxValue)
                .ThenBy(info => info.Id),
            LibrarySearchSort.EpisodeAscending => query
                .OrderBy(info => info.Season ?? int.MaxValue)
                .ThenBy(info => info.Episode ?? int.MaxValue)
                .ThenBy(info => info.Animation == null ? info.Title : info.Animation.Name)
                .ThenBy(info => info.Id),
            LibrarySearchSort.ScoreDescending => query
                .OrderByDescending(info => info.ReleaseScore)
                .ThenByDescending(info => info.PublishTime)
                .ThenByDescending(info => info.Id),
            _ => query
                .OrderByDescending(info => info.PublishTime)
                .ThenByDescending(info => info.Id)
        };

        var page = await ordered
            .Select(info => new SearchRow(
                info.Id,
                info.Title,
                info.Animation == null ? info.Title : info.Animation.Name,
                info.Animation == null ? null : info.Animation.Name,
                info.Animation == null ? null : info.Animation.OriginalName,
                info.Animation == null ? null : info.Animation.TmdbId,
                info.Season,
                info.Episode,
                info.ReleaseSubtitleGroup,
                info.Group == null ? null : info.Group.Name,
                info.ReleaseResolution,
                info.ReleaseCodec,
                info.ReleaseLanguages,
                info.IsDownloadTracked,
                info.IsDownloadFinished,
                info.DownloadType,
                info.ReleaseScore,
                info.ReleaseScoreReasonsJson,
                info.PublishTime))
            .Take(request.Take + 1)
            .ToListAsync(cancellationToken);
        var hasMore = page.Count > request.Take;
        if (hasMore) page.RemoveAt(page.Count - 1);
        var ids = page.Select(info => info.Id).ToArray();
        var mappingRows = ids.Length == 0
            ? []
            : await context.Database.SqlQuery<MappingPathRow>(
                    $"""
                     SELECT ranked."AnimationInfoId", ranked."VirtualPath", ranked."TotalCount"
                     FROM (
                         SELECT mapping."AnimationInfoId",
                                mapping."VirtualPath",
                                count(*) OVER (
                                    PARTITION BY mapping."AnimationInfoId") AS "TotalCount",
                                row_number() OVER (
                                    PARTITION BY mapping."AnimationInfoId"
                                    ORDER BY mapping."VirtualPath", mapping."Id") AS "RowNumber"
                         FROM "FileMappings" AS mapping
                         WHERE mapping."AnimationInfoId" = ANY ({ids})
                     ) AS ranked
                     WHERE ranked."RowNumber" <= {MaximumReturnedPathsPerRelease}
                     ORDER BY ranked."AnimationInfoId", ranked."VirtualPath"
                     """)
                .ToListAsync(cancellationToken);
        var mappingsByRelease = mappingRows
            .GroupBy(mapping => mapping.AnimationInfoId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.VirtualPath).ToList());
        var mappingCountsByRelease = mappingRows
            .GroupBy(mapping => mapping.AnimationInfoId)
            .ToDictionary(group => group.Key, group => group.First().TotalCount);
        var progressByRelease = (await context.PlaybackProgresses.AsNoTracking()
                .Where(progress => progress.UserId == request.UserId && ids.Contains(progress.AnimationInfoId))
                .OrderByDescending(progress => progress.UpdatedAt)
                .ToListAsync(cancellationToken))
            .GroupBy(progress => progress.AnimationInfoId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var items = page.Select(info =>
        {
            var progress = progressByRelease.GetValueOrDefault(info.Id) ?? [];
            return new LibrarySearchItem(
                info.Id,
                info.Title,
                info.AnimationName,
                info.AnimationOriginalName,
                info.TmdbId,
                info.Season,
                info.Episode,
                info.ReleaseSubtitleGroup ?? info.GroupName,
                info.ReleaseResolution,
                info.ReleaseCodec,
                info.ReleaseLanguages,
                info.IsDownloadTracked,
                info.IsDownloadFinished,
                info.DownloadType == FileDownloadTypes.MediaLibraryImport,
                progress.Any(item => item.IsWatched),
                progress.Count == 0 ? null : progress.Max(item => item.PositionSeconds),
                mappingsByRelease.GetValueOrDefault(info.Id) ?? [],
                mappingCountsByRelease.GetValueOrDefault(info.Id),
                info.ReleaseScore,
                ParseReasons(info.ReleaseScoreReasonsJson),
                info.PublishTime);
        }).ToList();

        if (await ReadLibraryRevisionAsync(cancellationToken) != revision)
            throw new ArgumentException("The library changed; restart search pagination.", nameof(request));
        if (await ReadWatchRevisionAsync(request.UserId, request.WatchState, cancellationToken)
            != watchRevision)
            throw new ArgumentException("The playback state changed; restart search pagination.", nameof(request));

        var nextCursor = hasMore
            ? EncodeCursor(CreateCursor(
                page[^1],
                request.Sort,
                snapshot,
                signature,
                revision,
                watchRevision))
            : null;
        return new LibrarySearchResult(items, nextCursor);
    }

    private static IQueryable<Models.AnimationInfo> SeekAfter(
        IQueryable<Models.AnimationInfo> query,
        LibrarySearchSort sort,
        SearchCursor cursor)
    {
        return sort switch
        {
            LibrarySearchSort.ScoreDescending when cursor.Score is { } score &&
                                                       cursor.PublishedAt is { } publishedAt =>
                query.Where(info => EF.Functions.LessThan(
                    ValueTuple.Create(info.ReleaseScore, info.PublishTime, info.Id),
                    ValueTuple.Create(score, publishedAt, cursor.LastId))),
            LibrarySearchSort.TitleAscending when cursor.SortTitle is { } sortTitle =>
                query.Where(info => EF.Functions.GreaterThan(
                    ValueTuple.Create(
                        info.Animation == null ? info.Title : info.Animation.Name,
                        info.Season ?? int.MaxValue,
                        info.Episode ?? int.MaxValue,
                        info.Id
                    ),
                    ValueTuple.Create(
                        sortTitle,
                        cursor.Season ?? int.MaxValue,
                        cursor.Episode ?? int.MaxValue,
                        cursor.LastId
                    ))),
            LibrarySearchSort.EpisodeAscending when cursor.SortTitle is { } sortTitle =>
                query.Where(info => EF.Functions.GreaterThan(
                    ValueTuple.Create(
                        info.Season ?? int.MaxValue,
                        info.Episode ?? int.MaxValue,
                        info.Animation == null ? info.Title : info.Animation.Name,
                        info.Id
                    ),
                    ValueTuple.Create(
                        cursor.Season ?? int.MaxValue,
                        cursor.Episode ?? int.MaxValue,
                        sortTitle,
                        cursor.LastId
                    ))),
            LibrarySearchSort.PublishedDescending when cursor.PublishedAt is { } publishedAt =>
                query.Where(info => EF.Functions.LessThan(
                    ValueTuple.Create(info.PublishTime, info.Id),
                    ValueTuple.Create(publishedAt, cursor.LastId))),
            _ => throw new ArgumentException("The search cursor is invalid.")
        };
    }

    private static SearchCursor CreateCursor(
        SearchRow row,
        LibrarySearchSort sort,
        DateTimeOffset snapshot,
        string signature,
        long revision,
        long? watchRevision) =>
        new(
            snapshot,
            signature,
            revision,
            watchRevision,
            row.Id,
            sort is LibrarySearchSort.PublishedDescending or LibrarySearchSort.ScoreDescending
                ? row.PublishTime
                : null,
            sort == LibrarySearchSort.ScoreDescending ? row.ReleaseScore : null,
            sort is LibrarySearchSort.TitleAscending or LibrarySearchSort.EpisodeAscending
                ? row.SortTitle
                : null,
            sort is LibrarySearchSort.TitleAscending or LibrarySearchSort.EpisodeAscending
                ? row.Season
                : null,
            sort is LibrarySearchSort.TitleAscending or LibrarySearchSort.EpisodeAscending
                ? row.Episode
                : null);

    private async Task<long> ReadLibraryRevisionAsync(CancellationToken cancellationToken) =>
        await context.AnimationCatalogStates.AsNoTracking()
            .Where(state => state.Id == 1)
            .Select(state => state.Revision)
            .SingleAsync(cancellationToken);

    private async Task<long?> ReadWatchRevisionAsync(
        Guid userId,
        LibraryWatchState watchState,
        CancellationToken cancellationToken)
    {
        if (watchState == LibraryWatchState.Any) return null;
        return await context.PlaybackCatalogStates.AsNoTracking()
            .Where(state => state.UserId == userId)
            .Select(state => (long?)state.Revision)
            .SingleOrDefaultAsync(cancellationToken) ?? 0;
    }

    public async Task<IReadOnlyList<LibraryIntegritySummary>> GetIntegrityAsync(
        string? tmdbId,
        int? season,
        CancellationToken cancellationToken)
    {
        var query = context.AnimationInfo.AsNoTracking()
            .Where(info => info.Animation != null && info.MediaLibraryMissingSince == null);
        if (!string.IsNullOrWhiteSpace(tmdbId))
            query = query.Where(info => info.Animation!.TmdbId == tmdbId);
        if (season is not null) query = query.Where(info => info.Season == season);

        var releaseIds = query.Select(info => info.Id);
        var releases = await query
            .Select(info => new IntegrityRelease(
                info.Id,
                info.Animation!.TmdbId,
                info.Animation.Name,
                info.Season,
                info.Episode,
                info.ExpectedEpisodeCount,
                info.IsDownloadFinished,
                info.IsActiveRelease,
                info.DownloadCancellationId,
                info.ReleaseScore,
                info.PublishTime,
                info.SourceFeedId,
                info.ReleaseScoreReasonsJson))
            .ToListAsync(cancellationToken);
        var mappedIds = await context.FileMappings.AsNoTracking()
            .Where(mapping => releaseIds.Contains(mapping.AnimationInfoId))
            .Select(mapping => mapping.AnimationInfoId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);
        var policies = await context.SubscriptionAutomationPolicies.AsNoTracking()
            .ToDictionaryAsync(policy => policy.FeedId, cancellationToken);
        var unavailableCandidateIds = await context.ReleaseUpgradeOperations.AsNoTracking()
            .Where(operation =>
                releaseIds.Contains(operation.CandidateReleaseId) &&
                operation.Status != ReleaseUpgradeStatus.Failed)
            .Select(operation => operation.CandidateReleaseId)
            .Distinct()
            .ToHashSetAsync(cancellationToken);

        return releases
            .Where(info => info.Season is > 0)
            .GroupBy(info => new
            {
                info.TmdbId,
                info.AnimationName,
                Season = info.Season!.Value
            })
            .Select(group => BuildIntegrity(group, mappedIds, policies, unavailableCandidateIds))
            .OrderBy(item => item.AnimationName)
            .ThenBy(item => item.Season)
            .ToList();
    }

    private static LibraryIntegritySummary BuildIntegrity(
        IEnumerable<IntegrityRelease> source,
        IReadOnlySet<Guid> mappedIds,
        IReadOnlyDictionary<Guid, Models.SubscriptionAutomationPolicy> policies,
        IReadOnlySet<Guid> unavailableCandidateIds)
    {
        var releases = source.ToList();
        var first = releases[0];
        var downloaded = releases
            .Where(info => info.IsDownloadFinished && mappedIds.Contains(info.Id) && info.Episode is > 0)
            .ToList();
        var expected = releases.Max(info => info.ExpectedEpisodeCount);
        var present = downloaded.Select(info => info.Episode!.Value).ToHashSet();
        var missing = expected is { } count
            ? Enumerable.Range(1, count).Where(episode => !present.Contains(episode)).ToList()
            : [];
        var duplicates = downloaded
            .GroupBy(info => info.Episode!.Value)
            .Where(group => group.Count() > 1)
            .Select(group => new EpisodeDuplicate(
                group.Key,
                group.OrderByDescending(item => item.ReleaseScore).Select(item => item.Id).ToList()))
            .OrderBy(item => item.Episode)
            .ToList();
        var candidates = new List<ReleaseUpgradeCandidate>();
        foreach (var episodeGroup in releases.Where(info => info.Episode is > 0).GroupBy(info => info.Episode!.Value))
        {
            var current = episodeGroup
                .Where(info => info.IsActiveRelease && info.IsDownloadFinished && mappedIds.Contains(info.Id))
                .OrderByDescending(info => info.ReleaseScore)
                .ThenByDescending(info => info.PublishTime)
                .FirstOrDefault();
            if (current is null) continue;
            var candidate = episodeGroup
                .Where(info =>
                    !info.IsActiveRelease &&
                    info.DownloadCancellationId is null &&
                    !unavailableCandidateIds.Contains(info.Id) &&
                    info.ReleaseScore > current.ReleaseScore)
                .OrderByDescending(info => info.ReleaseScore)
                .ThenByDescending(info => info.PublishTime)
                .FirstOrDefault();
            if (candidate is null) continue;
            var automatic = current.ReleaseScoreReasonsJson is not null &&
                            candidate.SourceFeedId is { } feedId &&
                            policies.TryGetValue(feedId, out var policy) &&
                            policy.EnableVersionUpgrade &&
                            candidate.ReleaseScore - current.ReleaseScore >= policy.MinimumUpgradeScore;
            candidates.Add(new ReleaseUpgradeCandidate(
                current.Id,
                candidate.Id,
                first.AnimationName,
                first.Season!.Value,
                episodeGroup.Key,
                current.ReleaseScore,
                candidate.ReleaseScore,
                ParseReasons(candidate.ReleaseScoreReasonsJson),
                automatic));
        }

        return new LibraryIntegritySummary(
            first.TmdbId,
            first.AnimationName,
            first.Season!.Value,
            expected,
            missing,
            duplicates,
            releases.Count(info => info.Episode is null),
            candidates.OrderBy(item => item.Episode).ToList());
    }

    private static string ContainsPattern(string value) =>
        $"%{value.Trim().Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)}%";

    private static IReadOnlyList<string> ParseReasons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Signature(LibrarySearchRequest request)
    {
        var value = string.Join('\n',
            request.Query?.Trim(), request.Season, request.Episode,
            request.SubtitleGroup, request.Resolution, request.Codec, request.Language,
            request.DownloadState, request.WatchState, request.VirtualPath,
            request.Source, request.Sort, request.Take, request.UserId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24];
    }

    private static string EncodeCursor(SearchCursor cursor)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cursor);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static SearchCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Length > 512) throw new ArgumentException("The search cursor is too long.");
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
            return JsonSerializer.Deserialize<SearchCursor>(Convert.FromBase64String(normalized))
                   ?? throw new ArgumentException("The search cursor is invalid.");
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("The search cursor is invalid.", exception);
        }
    }
}
