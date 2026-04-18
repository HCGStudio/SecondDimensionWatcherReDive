using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlRoot("propfind", Namespace = WebDavConstants.DavNamespace)]
public sealed class PropFindRequest
{
    [XmlElement("allprop", Namespace = WebDavConstants.DavNamespace)]
    public AllPropMarker? AllProp { get; set; }

    [XmlElement("propname", Namespace = WebDavConstants.DavNamespace)]
    public PropNameMarker? PropName { get; set; }

    [XmlElement("prop", Namespace = WebDavConstants.DavNamespace)]
    public Prop? Prop { get; set; }

    [XmlElement("include", Namespace = WebDavConstants.DavNamespace)]
    public IncludeProp? Include { get; set; }
}

[XmlType("allprop", Namespace = WebDavConstants.DavNamespace)]
public sealed class AllPropMarker;

[XmlType("propname", Namespace = WebDavConstants.DavNamespace)]
public sealed class PropNameMarker;

[XmlType("include", Namespace = WebDavConstants.DavNamespace)]
public sealed class IncludeProp
{
    [XmlAnyElement]
    public List<System.Xml.XmlElement> Properties { get; set; } = [];
}
