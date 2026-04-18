using SecondDimensionWatcherReDive.WebDav.Xml;

namespace SecondDimensionWatcherReDive.WebDav.Results;

public sealed class LockedResult : WebDavXmlResult<DavError>
{
    public LockedResult() : base(WebDavStatusCodes.Locked, null) { }

    public LockedResult(DavError error) : base(WebDavStatusCodes.Locked, error) { }
}

public sealed class FailedDependencyResult : WebDavXmlResult<MultiStatus>
{
    public FailedDependencyResult() : base(WebDavStatusCodes.FailedDependency, null) { }

    public FailedDependencyResult(MultiStatus value) : base(WebDavStatusCodes.FailedDependency, value) { }
}

public sealed class InsufficientStorageResult : WebDavXmlResult<DavError>
{
    public InsufficientStorageResult() : base(WebDavStatusCodes.InsufficientStorage, null) { }

    public InsufficientStorageResult(DavError error) : base(WebDavStatusCodes.InsufficientStorage, error) { }
}
