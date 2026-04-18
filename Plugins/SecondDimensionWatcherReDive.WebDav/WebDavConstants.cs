namespace SecondDimensionWatcherReDive.WebDav;

public static class WebDavConstants
{
    public const string DavNamespace = "DAV:";
    public const string DavNamespacePrefix = "d";
    public const string Win32Namespace = "urn:schemas-microsoft-com:";
    public const string Win32NamespacePrefix = "z";
    public const string ApacheDavNamespace = "http://apache.org/dav/props/";
    public const string ApacheDavNamespacePrefix = "a";
    public const string XmlContentType = "application/xml; charset=utf-8";

    public static class Headers
    {
        public const string Depth = "Depth";
        public const string Destination = "Destination";
        public const string Overwrite = "Overwrite";
        public const string If = "If";
        public const string LockToken = "Lock-Token";
        public const string Timeout = "Timeout";
        public const string Dav = "DAV";
    }

    public static class Depth
    {
        public const string Zero = "0";
        public const string One = "1";
        public const string Infinity = "infinity";
    }

    public static class Timeout
    {
        public const string Infinite = "Infinite";
        public const string SecondPrefix = "Second-";
    }
}
