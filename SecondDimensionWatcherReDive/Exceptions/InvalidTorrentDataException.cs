namespace SecondDimensionWatcherReDive.Exceptions;

public class InvalidTorrentDataException : Exception
{
    public InvalidTorrentDataException(string url, string reason = "torrent data is empty")
        : base($"Invalid torrent data from {SafeHost(url)}: {reason}.")
    {
    }

    private static string SafeHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.IdnHost)
            ? uri.IdnHost
            : "an unknown host";
}
