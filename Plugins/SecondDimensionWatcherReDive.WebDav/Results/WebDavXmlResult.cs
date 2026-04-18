using System.Text;
using Microsoft.AspNetCore.Mvc;
using SecondDimensionWatcherReDive.WebDav.Xml;

namespace SecondDimensionWatcherReDive.WebDav.Results;

public class WebDavXmlResult<T> : ActionResult where T : class
{
    public WebDavXmlResult(int statusCode, T? value)
    {
        StatusCode = statusCode;
        Value = value;
    }

    public int StatusCode { get; }
    public T? Value { get; }

    public override async Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCode;

        if (Value is null)
        {
            await response.CompleteAsync();
            return;
        }

        response.ContentType = WebDavConstants.XmlContentType;
        var xml = WebDavXml.Serialize(Value);
        var bytes = Encoding.UTF8.GetBytes(xml);
        response.ContentLength = bytes.Length;
        await response.Body.WriteAsync(bytes, context.HttpContext.RequestAborted);
    }
}
