namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record DownloadCapacityEntryResponse(
    Guid ItemId,
    string Title,
    long? ExpectedBytes,
    string State,
    bool Paused,
    string ReasonCode);
