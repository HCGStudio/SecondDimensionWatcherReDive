using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlRoot("lockinfo", Namespace = WebDavConstants.DavNamespace)]
public sealed class LockInfo
{
    [XmlElement("lockscope", Namespace = WebDavConstants.DavNamespace)]
    public LockScope LockScope { get; set; } = new();

    [XmlElement("locktype", Namespace = WebDavConstants.DavNamespace)]
    public LockType LockType { get; set; } = new();

    [XmlElement("owner", Namespace = WebDavConstants.DavNamespace)]
    public Owner? Owner { get; set; }
}
