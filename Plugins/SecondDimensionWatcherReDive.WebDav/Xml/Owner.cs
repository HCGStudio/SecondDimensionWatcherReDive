using System.Xml;
using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("owner", Namespace = WebDavConstants.DavNamespace)]
public sealed class Owner
{
    [XmlText]
    public string? Text { get; set; }

    [XmlAnyElement]
    public List<XmlElement> Content { get; set; } = [];
}
