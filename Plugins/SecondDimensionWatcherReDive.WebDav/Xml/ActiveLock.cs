using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("activelock", Namespace = WebDavConstants.DavNamespace)]
public sealed class ActiveLock
{
    [XmlElement("lockscope", Namespace = WebDavConstants.DavNamespace)]
    public LockScope LockScope { get; set; } = new();

    [XmlElement("locktype", Namespace = WebDavConstants.DavNamespace)]
    public LockType LockType { get; set; } = new();

    [XmlElement("depth", Namespace = WebDavConstants.DavNamespace)]
    public string Depth { get; set; } = WebDavConstants.Depth.Zero;

    [XmlElement("owner", Namespace = WebDavConstants.DavNamespace)]
    public Owner? Owner { get; set; }

    [XmlElement("timeout", Namespace = WebDavConstants.DavNamespace)]
    public string? Timeout { get; set; }

    [XmlElement("locktoken", Namespace = WebDavConstants.DavNamespace)]
    public LockToken? LockToken { get; set; }

    [XmlElement("lockroot", Namespace = WebDavConstants.DavNamespace)]
    public LockRoot? LockRoot { get; set; }
}

[XmlType("lockroot", Namespace = WebDavConstants.DavNamespace)]
public sealed class LockRoot
{
    [XmlElement("href", Namespace = WebDavConstants.DavNamespace)]
    public string Href { get; set; } = string.Empty;
}
