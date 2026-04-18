using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("propstat", Namespace = WebDavConstants.DavNamespace)]
public sealed class PropStat
{
    [XmlElement("prop", Namespace = WebDavConstants.DavNamespace)]
    public Prop Prop { get; set; } = new();

    [XmlElement("status", Namespace = WebDavConstants.DavNamespace)]
    public string Status { get; set; } = string.Empty;

    [XmlElement("error", Namespace = WebDavConstants.DavNamespace)]
    public DavError? Error { get; set; }

    [XmlElement("responsedescription", Namespace = WebDavConstants.DavNamespace)]
    public string? ResponseDescription { get; set; }
}
