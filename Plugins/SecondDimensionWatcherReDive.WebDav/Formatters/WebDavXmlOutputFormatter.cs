using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;
using SecondDimensionWatcherReDive.WebDav.Xml;

namespace SecondDimensionWatcherReDive.WebDav.Formatters;

public sealed class WebDavXmlOutputFormatter : TextOutputFormatter
{
    public WebDavXmlOutputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/xml"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/xml"));
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    protected override bool CanWriteType(Type? type) =>
        type is not null && type.Namespace == typeof(MultiStatus).Namespace;

    public override async Task WriteResponseBodyAsync(
        OutputFormatterWriteContext context,
        Encoding selectedEncoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selectedEncoding);

        if (context.Object is null)
        {
            return;
        }

        var method = typeof(WebDavXml)
            .GetMethod(nameof(WebDavXml.Serialize), [context.ObjectType!])
            ?? typeof(WebDavXml)
                .GetMethods()
                .First(m => m.Name == nameof(WebDavXml.Serialize)
                            && m.GetParameters().Length == 1
                            && m.GetParameters()[0].ParameterType.IsGenericParameter)
                .MakeGenericMethod(context.ObjectType!);

        var xml = (string)method.Invoke(null, [context.Object])!;
        var bytes = selectedEncoding.GetBytes(xml);
        await context.HttpContext.Response.Body.WriteAsync(
            bytes,
            context.HttpContext.RequestAborted);
    }
}
