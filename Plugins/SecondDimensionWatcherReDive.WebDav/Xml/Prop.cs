using System.Xml;
using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

[XmlType("prop", Namespace = WebDavConstants.DavNamespace)]
public sealed class Prop
{
    [XmlElement("creationdate", Namespace = WebDavConstants.DavNamespace)]
    public string? CreationDate { get; set; }

    [XmlElement("displayname", Namespace = WebDavConstants.DavNamespace)]
    public string? DisplayName { get; set; }

    [XmlElement("getcontentlength", Namespace = WebDavConstants.DavNamespace)]
    public string? GetContentLength { get; set; }

    [XmlElement("getcontenttype", Namespace = WebDavConstants.DavNamespace)]
    public string? GetContentType { get; set; }

    [XmlElement("getetag", Namespace = WebDavConstants.DavNamespace)]
    public string? GetETag { get; set; }

    [XmlElement("getlastmodified", Namespace = WebDavConstants.DavNamespace)]
    public string? GetLastModified { get; set; }

    [XmlElement("resourcetype", Namespace = WebDavConstants.DavNamespace)]
    public ResourceType? ResourceType { get; set; }

    [XmlElement("lockdiscovery", Namespace = WebDavConstants.DavNamespace)]
    public LockDiscovery? LockDiscovery { get; set; }

    [XmlElement("supportedlock", Namespace = WebDavConstants.DavNamespace)]
    public SupportedLock? SupportedLock { get; set; }

    [XmlElement("quota-available-bytes", Namespace = WebDavConstants.DavNamespace)]
    public string? QuotaAvailableBytes { get; set; }

    [XmlElement("quota-used-bytes", Namespace = WebDavConstants.DavNamespace)]
    public string? QuotaUsedBytes { get; set; }

    [XmlElement("Win32CreationTime", Namespace = WebDavConstants.Win32Namespace)]
    public string? Win32CreationTime { get; set; }

    [XmlElement("Win32LastAccessTime", Namespace = WebDavConstants.Win32Namespace)]
    public string? Win32LastAccessTime { get; set; }

    [XmlElement("Win32LastModifiedTime", Namespace = WebDavConstants.Win32Namespace)]
    public string? Win32LastModifiedTime { get; set; }

    [XmlElement("Win32FileAttributes", Namespace = WebDavConstants.Win32Namespace)]
    public string? Win32FileAttributes { get; set; }

    [XmlElement("executable", Namespace = WebDavConstants.ApacheDavNamespace)]
    public string? Executable { get; set; }

    [XmlAnyElement]
    public List<XmlElement> Extensions { get; set; } = [];
}
