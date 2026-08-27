namespace SecondDimensionWatcherReDive.Data;

public record struct RemoteTorrentTrackRequest(
    Guid ItemId,
    string Hash,
    bool Remove = false,
    Guid? DownloadAttemptId = null);
