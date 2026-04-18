namespace SecondDimensionWatcherReDive.WebDav.Http;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class HttpMoveAttribute : WebDavHttpMethodAttribute
{
    public HttpMoveAttribute() : base(WebDavMethods.Move) { }
    public HttpMoveAttribute(string template) : base(WebDavMethods.Move, template) { }
}
