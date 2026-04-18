using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("lockdiscovery", Namespace = WebDavConstants.DavNamespace)]
public sealed class LockDiscovery
{
    [XmlElement("activelock", Namespace = WebDavConstants.DavNamespace)]
    public List<ActiveLock> ActiveLocks { get; set; } = [];
}
