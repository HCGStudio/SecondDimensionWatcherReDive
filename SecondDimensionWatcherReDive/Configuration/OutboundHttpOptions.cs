using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Configuration;

internal sealed class OutboundHttpOptions
{
    internal const string SectionName = "OutboundHttp";

    [Range(0, 10)]
    public int MaxRedirects { get; set; } = 3;

    [Range(1, 300)]
    public int TotalTimeoutSeconds { get; set; } = 30;

    [Range(1, 60)]
    public int ConnectTimeoutSeconds { get; set; } = 10;

    [Range(1, 120)]
    public int FirstByteTimeoutSeconds { get; set; } = 15;

    [Range(1, 64)]
    public int MaxConcurrentRequests { get; set; } = 4;

    [Range(1, 64 * 1024 * 1024)]
    public int MaxFeedBytes { get; set; } = 4 * 1024 * 1024;

    [Range(1, 64 * 1024 * 1024)]
    public int MaxTorrentBytes { get; set; } = 8 * 1024 * 1024;

    [Range(1, 10_000)]
    public int MaxFeedItems { get; set; } = 1_000;

    public string[] AllowedPrivateHosts { get; set; } = [];

    public string[] AllowedPrivateNetworks { get; set; } = [];
}
