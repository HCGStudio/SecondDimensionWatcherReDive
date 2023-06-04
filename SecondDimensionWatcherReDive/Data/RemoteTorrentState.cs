namespace SecondDimensionWatcherReDive.Data;

public enum RemoteTorrentState
{
    Error,
    MissingFiles,
    Uploading,
    PausedUp,
    QueuedUp,
    StalledUp,
    CheckingUp,
    ForcedUp,
    Allocating,
    Downloading,
    MetaDl,
    PausedDl,
    QueuedDl,
    StalledDl,
    CheckingDl,
    ForcedDl,
    CheckingResumeData,
    Moving,
    Unknown
}

public static class RemoteTorrentStateExtension
{
    public static FileDownloadState ToDownloadState(this RemoteTorrentState remoteTorrentState)
    {
        return remoteTorrentState switch
        {
            RemoteTorrentState.Error or RemoteTorrentState.MissingFiles or RemoteTorrentState.Unknown =>
                FileDownloadState.Error,
            RemoteTorrentState.Uploading or RemoteTorrentState.PausedUp or RemoteTorrentState.QueuedUp
                or RemoteTorrentState.StalledUp or RemoteTorrentState.CheckingUp
                or RemoteTorrentState.ForcedUp or RemoteTorrentState.Moving => FileDownloadState.Finished,
            RemoteTorrentState.Allocating or RemoteTorrentState.Downloading or RemoteTorrentState.MetaDl
                or RemoteTorrentState.QueuedDl or RemoteTorrentState.StalledDl
                or RemoteTorrentState.CheckingDl or RemoteTorrentState.ForcedDl
                or RemoteTorrentState.CheckingResumeData => FileDownloadState.Downloading,
            RemoteTorrentState.PausedDl => FileDownloadState.Paused,
            _ => throw new ArgumentOutOfRangeException(nameof(remoteTorrentState), remoteTorrentState, null)
        };
    }
}