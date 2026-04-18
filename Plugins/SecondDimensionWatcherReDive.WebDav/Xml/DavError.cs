using System.Xml;
using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlRoot("error", Namespace = WebDavConstants.DavNamespace)]
[XmlType("error", Namespace = WebDavConstants.DavNamespace)]
public sealed class DavError
{
    [XmlAnyElement]
    public List<XmlElement> Conditions { get; set; } = [];
}
