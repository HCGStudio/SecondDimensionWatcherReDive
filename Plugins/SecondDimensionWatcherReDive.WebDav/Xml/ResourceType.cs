using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("resourcetype", Namespace = WebDavConstants.DavNamespace)]
public sealed class ResourceType
{
    [XmlElement("collection", Namespace = WebDavConstants.DavNamespace)]
    public CollectionMarker? Collection { get; set; }

    [XmlIgnore]
    public bool IsCollection
    {
        get => Collection is not null;
        set => Collection = value ? new CollectionMarker() : null;
    }
}

[XmlType("collection", Namespace = WebDavConstants.DavNamespace)]
public sealed class CollectionMarker;
