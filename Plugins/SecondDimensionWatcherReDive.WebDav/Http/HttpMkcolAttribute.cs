namespace SecondDimensionWatcherReDive.WebDav.Http;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpMkcolAttribute : WebDavHttpMethodAttribute
{
    public HttpMkcolAttribute() : base(WebDavMethods.MkCol) { }
    public HttpMkcolAttribute(string template) : base(WebDavMethods.MkCol, template) { }
}
