namespace SecondDimensionWatcherReDive.WebDav.Http;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpPropFindAttribute : WebDavHttpMethodAttribute
{
    public HttpPropFindAttribute() : base(WebDavMethods.PropFind) { }
    public HttpPropFindAttribute(string template) : base(WebDavMethods.PropFind, template) { }
}
