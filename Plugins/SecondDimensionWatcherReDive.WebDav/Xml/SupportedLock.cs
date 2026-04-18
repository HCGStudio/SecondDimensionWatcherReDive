using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("supportedlock", Namespace = WebDavConstants.DavNamespace)]
public sealed class SupportedLock
{
    [XmlElement("lockentry", Namespace = WebDavConstants.DavNamespace)]
    public List<LockEntry> Entries { get; set; } = [];
}

[XmlType("lockentry", Namespace = WebDavConstants.DavNamespace)]
public sealed class LockEntry
{
    [XmlElement("lockscope", Namespace = WebDavConstants.DavNamespace)]
    public LockScope LockScope { get; set; } = new();

    [XmlElement("locktype", Namespace = WebDavConstants.DavNamespace)]
    public LockType LockType { get; set; } = new();
}
