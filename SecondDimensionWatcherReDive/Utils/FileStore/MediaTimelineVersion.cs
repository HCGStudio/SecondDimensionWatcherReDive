using System.Security.Cryptography;
using System.Text;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

internal static class MediaTimelineVersion
{
    public static async Task<string?> ResolveAsync(FileMapping mapping, IFileStoreProvider stores,
        CancellationToken cancellationToken)
    {
        FileStoreInfo metadata;
        try { metadata = await stores.GetRequiredClient(mapping.FileStore).FileInfoAsync(mapping.PhysicalPath, cancellationToken); }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
        if (metadata.IsDirectory || !metadata.Length.HasValue || !metadata.LastModifiedUtc.HasValue) return null;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{mapping.Id}\0{mapping.AnimationInfoId}\0{mapping.VirtualPath}\0{mapping.FileStore}\0{mapping.PhysicalPath}\0{metadata.Length}\0{metadata.LastModifiedUtc:O}")));
    }
}
