namespace SecondDimensionWatcherReDive.WebDav.Http;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpPropPatchAttribute : WebDavHttpMethodAttribute
{
    public HttpPropPatchAttribute() : base(WebDavMethods.PropPatch) { }
    public HttpPropPatchAttribute(string template) : base(WebDavMethods.PropPatch, template) { }
}
