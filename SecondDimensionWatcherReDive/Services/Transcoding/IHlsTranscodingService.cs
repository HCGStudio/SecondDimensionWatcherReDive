namespace SecondDimensionWatcherReDive.Services.Transcoding;

internal interface IHlsTranscodingService
{
    Task<TranscodingSessionStatus> PrepareAsync(
        Guid animationInfoId,
        string? relativePath,
        TranscodingSelection selection,
        CancellationToken cancellationToken);

    Task<TranscodingSessionStatus?> GetStatusAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken);

    Task<string?> GetPlaylistAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken);

    Task<TranscodingContent?> OpenSegmentAsync(
        Guid sessionId,
        string accessToken,
        string fileName,
        CancellationToken cancellationToken);

    Task<TranscodingContent?> OpenSubtitleAsync(
        Guid sessionId,
        string accessToken,
        string fileName,
        CancellationToken cancellationToken);

    Task<TranscodingContent?> OpenDirectAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken);

    Task<bool> CancelAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken);

    Task<TranscodingMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken);
}
