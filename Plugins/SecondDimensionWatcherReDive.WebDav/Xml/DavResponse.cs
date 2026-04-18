using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("response", Namespace = WebDavConstants.DavNamespace)]
public sealed class DavResponse
{
    [XmlElement("href", Namespace = WebDavConstants.DavNamespace)]
    public string Href { get; set; } = string.Empty;

    [XmlElement("propstat", Namespace = WebDavConstants.DavNamespace)]
    public List<PropStat> PropStats { get; set; } = [];

    [XmlElement("status", Namespace = WebDavConstants.DavNamespace)]
    public string? Status { get; set; }

    [XmlElement("error", Namespace = WebDavConstants.DavNamespace)]
    public DavError? Error { get; set; }

    [XmlElement("responsedescription", Namespace = WebDavConstants.DavNamespace)]
    public string? ResponseDescription { get; set; }

    [XmlElement("location", Namespace = WebDavConstants.DavNamespace)]
    public DavLocation? Location { get; set; }
}

[XmlType("location", Namespace = WebDavConstants.DavNamespace)]
public sealed class DavLocation
{
    [XmlElement("href", Namespace = WebDavConstants.DavNamespace)]
    public string Href { get; set; } = string.Empty;
}
