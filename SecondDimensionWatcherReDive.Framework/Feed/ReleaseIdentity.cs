using System.Security.Cryptography;
using System.Text;

namespace SecondDimensionWatcherReDive.Framework.Feed;

public static class ReleaseIdentity
{
    public static string Create(
        Guid? feedId,
        string? feedItemGuid,
        string? enclosureId,
        string? torrentInfoHash,
        string downloadUrl)
    {
        if (!string.IsNullOrWhiteSpace(torrentInfoHash))
            return $"torrent:{torrentInfoHash.Trim().ToLowerInvariant()}";

        var source = !string.IsNullOrWhiteSpace(feedItemGuid)
            ? $"feed:{feedId?.ToString("N") ?? "static"}:{feedItemGuid.Trim()}"
            : !string.IsNullOrWhiteSpace(enclosureId)
                ? $"enclosure:{feedId?.ToString("N") ?? "static"}:{enclosureId.Trim()}"
                : $"url:{downloadUrl.Trim()}";
        return $"external:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()}";
    }

    public static string CreateMediaImport(Guid sourceId, string fileStore, string storePath)
    {
        var source = $"{sourceId:N}\n{fileStore}\n{storePath}";
        return $"import:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant()}";
    }
}
