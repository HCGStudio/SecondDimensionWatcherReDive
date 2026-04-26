namespace SecondDimensionWatcherReDive.NFS.Xdr;

internal sealed class XdrException : Exception
{
    public XdrException(string message) : base(message) { }
    public XdrException(string message, Exception inner) : base(message, inner) { }
}
