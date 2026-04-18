using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlRoot("multistatus", Namespace = WebDavConstants.DavNamespace)]
public sealed class MultiStatus
{
    [XmlElement("response", Namespace = WebDavConstants.DavNamespace)]
    public List<DavResponse> Responses { get; set; } = [];

    [XmlElement("responsedescription", Namespace = WebDavConstants.DavNamespace)]
    public string? ResponseDescription { get; set; }
}
