using Microsoft.AspNetCore.Mvc.Routing;

namespace SecondDimensionWatcherReDive.WebDav.Http;

public abstract class WebDavHttpMethodAttribute : HttpMethodAttribute
{
    protected WebDavHttpMethodAttribute(string method)
        : base([method])
    {
    }

    protected WebDavHttpMethodAttribute(string method, string template)
        : base([method], template)
    {
    }
}
