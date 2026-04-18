using System.Collections.Concurrent;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace SecondDimensionWatcherReDive.WebDav.Xml;

public static class WebDavXml
{
    private static readonly ConcurrentDictionary<Type, XmlSerializer> SerializerCache = new();

    private static readonly XmlSerializerNamespaces Namespaces = CreateNamespaces();

    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = false,
        OmitXmlDeclaration = false,
        NewLineHandling = NewLineHandling.Entitize
    };

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true
    };

    public static string Serialize<T>(T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, WriterSettings))
        {
            GetSerializer(typeof(T)).Serialize(writer, value, Namespaces);
        }

        return WriterSettings.Encoding.GetString(buffer.ToArray());
    }

    public static void Serialize<T>(Stream output, T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(value);
        using var writer = XmlWriter.Create(output, WriterSettings);
        GetSerializer(typeof(T)).Serialize(writer, value, Namespaces);
    }

    public static T? Deserialize<T>(string xml) where T : class
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        using var reader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(reader, ReaderSettings);
        return (T?)GetSerializer(typeof(T)).Deserialize(xmlReader);
    }

    public static T? Deserialize<T>(Stream body) where T : class
    {
        ArgumentNullException.ThrowIfNull(body);
        if (body.CanSeek && body.Length == 0) return null;
        using var xmlReader = XmlReader.Create(body, ReaderSettings);
        if (!xmlReader.Read()) return null;
        xmlReader.MoveToContent();
        if (xmlReader.EOF) return null;
        return (T?)GetSerializer(typeof(T)).Deserialize(xmlReader);
    }

    private static XmlSerializer GetSerializer(Type type) =>
        SerializerCache.GetOrAdd(type, static t => new XmlSerializer(t));

    private static XmlSerializerNamespaces CreateNamespaces()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add(WebDavConstants.DavNamespacePrefix, WebDavConstants.DavNamespace);
        return namespaces;
    }
}
