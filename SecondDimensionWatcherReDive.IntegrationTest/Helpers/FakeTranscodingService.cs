using SecondDimensionWatcherReDive.Services.Transcoding;

namespace SecondDimensionWatcherReDive.IntegrationTest.Helpers;

internal sealed class FakeTranscodingService : IHlsTranscodingService
{
    public Guid SessionId { get; private set; } = Guid.NewGuid();
    public string Token { get; private set; } = "integration-transcoding-token";

    public void Reset()
    {
        SessionId = Guid.NewGuid();
        Token = "integration-transcoding-token";
    }

    public Task<TranscodingSessionStatus> PrepareAsync(
        Guid animationInfoId,
        string? relativePath,
        TranscodingSelection selection,
        CancellationToken cancellationToken)
        => Task.FromResult(CreateStatus());

    public Task<TranscodingSessionStatus?> GetStatusAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken)
        => Task.FromResult<TranscodingSessionStatus?>(IsValid(sessionId, accessToken) ? CreateStatus() : null);

    public Task<string?> GetPlaylistAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken)
        => Task.FromResult(IsValid(sessionId, accessToken)
            ? "#EXTM3U\n#EXTINF:6,\nsegment-000000.ts\n#EXT-X-ENDLIST\n"
            : null);

    public Task<TranscodingContent?> OpenSegmentAsync(
        Guid sessionId,
        string accessToken,
        string fileName,
        CancellationToken cancellationToken)
        => Task.FromResult<TranscodingContent?>(
            IsValid(sessionId, accessToken) && fileName == "segment-000000.ts"
                ? new TranscodingContent(
                    new MemoryStream([1, 2, 3], writable: false),
                    "video/mp2t",
                    fileName,
                    3,
                    DateTimeOffset.UnixEpoch)
                : null);

    public Task<TranscodingContent?> OpenSubtitleAsync(
        Guid sessionId,
        string accessToken,
        string fileName,
        CancellationToken cancellationToken)
        => Task.FromResult<TranscodingContent?>(null);

    public Task<TranscodingContent?> OpenDirectAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken)
        => Task.FromResult<TranscodingContent?>(null);

    public Task<bool> CancelAsync(
        Guid sessionId,
        string accessToken,
        CancellationToken cancellationToken)
        => Task.FromResult(IsValid(sessionId, accessToken));

    public Task<TranscodingMetricsSnapshot> GetMetricsAsync(CancellationToken cancellationToken)
        => Task.FromResult(new TranscodingMetricsSnapshot(
            0,
            0,
            4,
            1,
            1,
            2,
            4096,
            0.75,
            3.2,
            0.2));

    private bool IsValid(Guid sessionId, string accessToken)
        => sessionId == SessionId && accessToken == Token;

    private TranscodingSessionStatus CreateStatus()
        => new(
            SessionId,
            Token,
            TranscodingJobState.Ready,
            TranscodingStrategy.Remux,
            true,
            true,
            1,
            3.2,
            null,
            null,
            "h264",
            "aac",
            [],
            0);
}
