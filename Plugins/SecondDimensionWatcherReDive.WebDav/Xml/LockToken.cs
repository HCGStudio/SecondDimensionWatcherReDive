using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("locktoken", Namespace = WebDavConstants.DavNamespace)]
public sealed class LockToken
{
    [XmlElement("href", Namespace = WebDavConstants.DavNamespace)]
    public string Href { get; set; } = string.Empty;
}
