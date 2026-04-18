using SecondDimensionWatcherReDive.WebDav.Xml;

namespace SecondDimensionWatcherReDive.WebDav.Results;

public sealed class MultiStatusResult : WebDavXmlResult<MultiStatus>
{
    public MultiStatusResult(MultiStatus value)
        : base(WebDavStatusCodes.MultiStatus, value)
    {
    }
}
