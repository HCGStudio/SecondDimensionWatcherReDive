namespace SecondDimensionWatcherReDive.Utils.FileDownload;

public sealed class QBittorrentRemoteOptions
{
    public const string SectionName = "Torrent:Remote";

    public string Url { get; set; } = "http://localhost:8080";

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string? UserAgent { get; set; }
}
