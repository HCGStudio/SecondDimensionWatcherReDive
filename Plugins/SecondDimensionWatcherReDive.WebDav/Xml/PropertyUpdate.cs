using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlRoot("propertyupdate", Namespace = WebDavConstants.DavNamespace)]
public sealed class PropertyUpdate
{
    [XmlElement("set", typeof(SetOperation), Namespace = WebDavConstants.DavNamespace)]
    [XmlElement("remove", typeof(RemoveOperation), Namespace = WebDavConstants.DavNamespace)]
    public List<PropertyUpdateOperation> Operations { get; set; } = [];
}

public abstract class PropertyUpdateOperation
{
    [XmlElement("prop", Namespace = WebDavConstants.DavNamespace)]
    public Prop Prop { get; set; } = new();
}

[XmlType("set", Namespace = WebDavConstants.DavNamespace)]
public sealed class SetOperation : PropertyUpdateOperation;

[XmlType("remove", Namespace = WebDavConstants.DavNamespace)]
public sealed class RemoveOperation : PropertyUpdateOperation;
