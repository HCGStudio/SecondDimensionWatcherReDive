using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("lockscope", Namespace = WebDavConstants.DavNamespace)]
public sealed class LockScope
{
    [XmlElement("exclusive", Namespace = WebDavConstants.DavNamespace)]
    public ExclusiveMarker? Exclusive { get; set; }

    [XmlElement("shared", Namespace = WebDavConstants.DavNamespace)]
    public SharedMarker? Shared { get; set; }

    public static LockScope CreateExclusive() => new() { Exclusive = new ExclusiveMarker() };
    public static LockScope CreateShared() => new() { Shared = new SharedMarker() };
}

[XmlType("exclusive", Namespace = WebDavConstants.DavNamespace)]
public sealed class ExclusiveMarker;

[XmlType("shared", Namespace = WebDavConstants.DavNamespace)]
public sealed class SharedMarker;
