using System.Net;

namespace SecondDimensionWatcherReDive.Utils.FileDownload;

public sealed class QBittorrentCookieStore
{
    public CookieContainer Container { get; } = new();

    public void Clear()
    {
        foreach (Cookie cookie in Container.GetAllCookies())
            cookie.Expired = true;
    }
}
