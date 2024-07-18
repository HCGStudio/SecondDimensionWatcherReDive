using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Framework.Plugin;
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
    public async Task<bool> SubmitDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo)
    {
        await _beforeDownloadStartEventTrigger.Invoke(
            new FileDownloadStartParam(
                itemId,
                downloadUrl,
                cachedDownloadData, 
                additionalDownloadInfo));
        return await _poxyObject.SubmitDownloadTask(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo);
    }

    public async Task SubmitQueryDownloadProgress(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo)
    {
        await _poxyObject.SubmitQueryDownloadProgress(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo);
    }

    public async Task<bool> PauseDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo)
    {
        return await _poxyObject.PauseDownloadTask(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo);
    }

    public async Task<bool> ResumeDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo)
    {
        return await _poxyObject.ResumeDownloadTask(
            itemId,
            downloadUrl,
            cachedDownloadData,
            additionalDownloadInfo);
    }

    public Task<CancelDownloadResult> CancelDownloadTask(
        Guid itemId,
        string downloadUrl,
        byte[] cachedDownloadData,
        string additionalDownloadInfo,
        bool removeFile)
    {
        throw new NotImplementedException();
    }

    public static FileDownloadClientProxy Create(IFileDownloadClient client, IServiceProvider serviceProvider)
    {
        return new FileDownloadClientProxy(
            client,
            serviceProvider.GetRequiredService<IPluginEventTrigger<FileDownloadStartParam>>());
    }
}