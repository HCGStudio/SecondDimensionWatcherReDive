using SecondDimensionWatcherReDive.Data;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Utils.FileDownload;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public partial class TorrentFileOperator(
    IHttpClientFactory httpClientFactory,
    ILogger<TorrentFileOperator> logger) : IFileOperator
{
    private readonly HttpClient _httpClient =
        httpClientFactory.CreateClient(nameof(RemoteTorrentDownloadClient));

    public async Task<bool> Rename(string oldName, string newName)
    {
        try
        {
            var oldFullPath = Path.GetFullPath(oldName);
            var newFullPath = Path.GetFullPath(newName);

            // Query qBittorrent for all torrents to find the one owning this file
            var torrents = await _httpClient.GetFromJsonAsync(
                "/api/v2/torrents/info",
                QBittorrentJsonSerializerContext.Default.RemoteTorrentInfoArray);
            if (torrents is null || torrents.Length == 0)
            {
                LogNoTorrentsFound(logger, oldName);
                return false;
            }

            // Find the torrent whose content_path is a parent of the file
            var matchedTorrent = FindTorrentForPath(torrents, oldFullPath);
            if (matchedTorrent is null)
            {
                LogNoTorrentMatchingPath(logger, oldFullPath);
                return false;
            }

            // Compute the save directory (parent of content_path for single-file, or content_path for directory)
            var saveDir = GetSaveDirectory(matchedTorrent.SavePath, oldFullPath);

            var relativeOldPath = Path.GetRelativePath(saveDir, oldFullPath);
            var relativeNewPath = Path.GetRelativePath(saveDir, newFullPath);

            using var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("hash", matchedTorrent.Hash),
                new KeyValuePair<string, string>("oldPath", relativeOldPath),
                new KeyValuePair<string, string>("newPath", relativeNewPath)
            ]);

            using var response = await _httpClient.PostAsync("/api/v2/torrents/renameFile", content);

            if (response.IsSuccessStatusCode)
            {
                LogRenamedViaQBittorrent(logger, relativeOldPath, relativeNewPath, matchedTorrent.Hash);
                return true;
            }

            LogRenameFileReturnedError(logger, (int)response.StatusCode, relativeOldPath, relativeNewPath);
            return false;
        }
        catch (Exception ex)
        {
            LogRenameFileFailed(logger, ex, oldName, newName);
            return false;
        }
    }

    private static RemoteTorrentInfo? FindTorrentForPath(RemoteTorrentInfo[] torrents, string filePath)
    {
        // content_path can be the file itself (single-file torrent) or a directory (multi-file torrent)
        // Match the torrent whose content_path is a prefix of the file path,
        // or whose content_path's directory contains the file
        foreach (var torrent in torrents)
        {
            var contentPath = Path.GetFullPath(torrent.SavePath);

            // Exact match (single-file torrent where content_path IS the file)
            if (string.Equals(contentPath, filePath, StringComparison.Ordinal))
                return torrent;

            // The file is inside the torrent's content directory
            if (filePath.StartsWith(contentPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return torrent;

            // content_path is a file in the same directory
            var contentDir = Path.GetDirectoryName(contentPath);
            if (contentDir != null &&
                filePath.StartsWith(contentDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return torrent;
        }

        return null;
    }

    private static string GetSaveDirectory(string contentPath, string filePath)
    {
        var fullContentPath = Path.GetFullPath(contentPath);

        // If the file is inside the content_path directory, use content_path as the save dir
        if (filePath.StartsWith(fullContentPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return fullContentPath;

        // Otherwise, use the parent directory of content_path
        return Path.GetDirectoryName(fullContentPath) ?? fullContentPath;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No torrents found in qBittorrent, cannot rename {OldName}")]
    private static partial void LogNoTorrentsFound(ILogger logger, string oldName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No torrent found matching path {Path}, cannot rename via qBittorrent")]
    private static partial void LogNoTorrentMatchingPath(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Renamed via qBittorrent: {Old} -> {New} (torrent {Hash})")]
    private static partial void LogRenamedViaQBittorrent(ILogger logger, string old, string @new, string hash);

    [LoggerMessage(Level = LogLevel.Warning, Message = "qBittorrent renameFile returned {StatusCode} for {Old} -> {New}")]
    private static partial void LogRenameFileReturnedError(ILogger logger, int statusCode, string old, string @new);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to rename file from {OldName} to {NewName} via qBittorrent")]
    private static partial void LogRenameFileFailed(ILogger logger, Exception ex, string oldName, string newName);
}
