using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Plugin;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

public class FileDownloadClientProxy : IFileDownloadClient
{
    private readonly IFileDownloadClient _poxyObject;
    private readonly IPluginEventTrigger<FileDownloadStartParam> _beforeDownloadStartEventTrigger;
    private FileDownloadClientProxy(
        IFileDownloadClient poxyObject,
        IPluginEventTrigger<FileDownloadStartParam> beforeDownloadStartEventTrigger)
    {
        _poxyObject = poxyObject;
        _beforeDownloadStartEventTrigger = beforeDownloadStartEventTrigger;
    }

    public string Name => _poxyObject.Name;
    public string SupportedFileStoreType => _poxyObject.SupportedFileStoreType;
    public string FileDownloadType => _poxyObject.FileDownloadType;
    public async Task<bool> SubmitDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        await _beforeDownloadStartEventTrigger.InvokeAsync(
            new FileDownloadStartParam(
                itemId,
                downloadUrl,
                cachedDownloadData,
                additionalDownloadInfo), cancellationToken);
        return await _poxyObject.SubmitDownloadTaskAsync(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo,
            cancellationToken);
    }

    public Task<DownloadTaskReconciliationOutcome> EnsureDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken) =>
        _poxyObject.EnsureDownloadTaskAsync(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo,
            cancellationToken);

    public async Task SubmitQueryDownloadProgressAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        await _poxyObject.SubmitQueryDownloadProgressAsync(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo,
            cancellationToken);
    }

    public async Task<bool> PauseDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        return await _poxyObject.PauseDownloadTaskAsync(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo,
            cancellationToken);
    }

    public async Task<bool> ResumeDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        CancellationToken cancellationToken)
    {
        return await _poxyObject.ResumeDownloadTaskAsync(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo,
            cancellationToken);
    }

    public async Task<CancelDownloadResult> CancelDownloadTaskAsync(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        bool removeFile,
        CancellationToken cancellationToken)
    {
        return await _poxyObject.CancelDownloadTaskAsync(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo,
            removeFile,
            cancellationToken);
    }

    public static FileDownloadClientProxy Create(IFileDownloadClient client, IServiceProvider serviceProvider)
    {
        return new FileDownloadClientProxy(
            client,
            serviceProvider.GetRequiredService<IPluginEventTrigger<FileDownloadStartParam>>());
    }
}
