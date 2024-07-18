namespace SecondDimensionWatcherReDive.Exceptions;

public class InvalidTorrentDataException(string url) : Exception($"{url} seems to be empty data.");