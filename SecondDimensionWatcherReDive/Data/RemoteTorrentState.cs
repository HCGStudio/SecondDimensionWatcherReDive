using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Data;

public enum RemoteTorrentState
{
    [JsonStringEnumMemberName("error")]
    Error,
    [JsonStringEnumMemberName("missingFiles")]
    MissingFiles,
    [JsonStringEnumMemberName("uploading")]
    Uploading,
    [JsonStringEnumMemberName("pausedUP")]
    PausedUp,
    [JsonStringEnumMemberName("queuedUP")]
    QueuedUp,
    [JsonStringEnumMemberName("stalledUP")]
    StalledUp,
    [JsonStringEnumMemberName("checkingUP")]
    CheckingUp,
    [JsonStringEnumMemberName("forcedUP")]
    ForcedUp,
    [JsonStringEnumMemberName("allocating")]
    Allocating,
    [JsonStringEnumMemberName("downloading")]
    Downloading,
    [JsonStringEnumMemberName("metaDL")]
    MetaDl,
    [JsonStringEnumMemberName("pausedDL")]
    PausedDl,
    [JsonStringEnumMemberName("queuedDL")]
    QueuedDl,
    [JsonStringEnumMemberName("stalledDL")]
    StalledDl,
    [JsonStringEnumMemberName("checkingDL")]
    CheckingDl,
    [JsonStringEnumMemberName("forcedDL")]
    ForcedDl,
    [JsonStringEnumMemberName("checkingResumeData")]
    CheckingResumeData,
    [JsonStringEnumMemberName("moving")]
    Moving,
    [JsonStringEnumMemberName("unknown")]
    Unknown,
    [JsonStringEnumMemberName("stoppedUP")]
    StoppedUp,
    [JsonStringEnumMemberName("stoppedDL")]
    StoppedDl
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
                or RemoteTorrentState.ForcedUp or RemoteTorrentState.StoppedUp
                or RemoteTorrentState.Moving => FileDownloadState.Finished,
            RemoteTorrentState.Allocating or RemoteTorrentState.Downloading or RemoteTorrentState.MetaDl
                or RemoteTorrentState.QueuedDl or RemoteTorrentState.StalledDl
                or RemoteTorrentState.CheckingDl or RemoteTorrentState.ForcedDl
                or RemoteTorrentState.CheckingResumeData => FileDownloadState.Downloading,
            RemoteTorrentState.PausedDl or RemoteTorrentState.StoppedDl => FileDownloadState.Paused,
            _ => throw new ArgumentOutOfRangeException(nameof(remoteTorrentState), remoteTorrentState, null)
        };
    }
}