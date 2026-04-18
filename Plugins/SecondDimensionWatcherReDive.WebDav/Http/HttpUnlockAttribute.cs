namespace SecondDimensionWatcherReDive.WebDav.Http;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpUnlockAttribute : WebDavHttpMethodAttribute
{
    public HttpUnlockAttribute() : base(WebDavMethods.Unlock) { }
    public HttpUnlockAttribute(string template) : base(WebDavMethods.Unlock, template) { }
}
