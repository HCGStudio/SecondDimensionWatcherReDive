namespace SecondDimensionWatcherReDive.WebDav.Http;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpLockAttribute : WebDavHttpMethodAttribute
{
    public HttpLockAttribute() : base(WebDavMethods.Lock) { }
    public HttpLockAttribute(string template) : base(WebDavMethods.Lock, template) { }
}
