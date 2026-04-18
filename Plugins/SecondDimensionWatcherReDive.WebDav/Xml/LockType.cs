using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("locktype", Namespace = WebDavConstants.DavNamespace)]
public sealed class LockType
{
    [XmlElement("write", Namespace = WebDavConstants.DavNamespace)]
    public WriteMarker? Write { get; set; }

    public static LockType CreateWrite() => new() { Write = new WriteMarker() };
}

[XmlType("write", Namespace = WebDavConstants.DavNamespace)]
public sealed class WriteMarker;
