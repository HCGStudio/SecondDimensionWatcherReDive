namespace SecondDimensionWatcherReDive.WebDav.Http;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpCopyAttribute : WebDavHttpMethodAttribute
{
    public HttpCopyAttribute() : base(WebDavMethods.Copy) { }
    public HttpCopyAttribute(string template) : base(WebDavMethods.Copy, template) { }
}
