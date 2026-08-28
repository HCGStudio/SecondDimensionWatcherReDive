namespace SecondDimensionWatcherReDive.Exceptions;

public class InvalidTorrentDataException(string url, string reason = "torrent data is empty")
    : Exception($"Invalid torrent data from {url}: {reason}.");
