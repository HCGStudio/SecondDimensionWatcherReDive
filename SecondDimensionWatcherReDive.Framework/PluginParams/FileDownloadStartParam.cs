namespace SecondDimensionWatcherReDive.Framework.PluginParams;

public class FileDownloadStartParam(
    Guid id,
    string downloadUrl,
    byte[] cachedDownloadData,
    string additionalDownloadInfo)
{
    public Guid Id { get; set; } = id;

    public string DownloadUrl { get; set; } = downloadUrl;

    public byte[] CachedDownloadData { get; set; } = cachedDownloadData;

    public string AdditionalDownloadInfo { get; set; } = additionalDownloadInfo;
}