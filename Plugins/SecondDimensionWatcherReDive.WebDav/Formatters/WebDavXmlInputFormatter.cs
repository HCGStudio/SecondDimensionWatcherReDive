using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;
using SecondDimensionWatcherReDive.WebDav.Xml;

namespace SecondDimensionWatcherReDive.WebDav.Formatters;

public sealed class WebDavXmlInputFormatter : TextInputFormatter
{
    public WebDavXmlInputFormatter()
    {
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/xml"));
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("text/xml"));
        SupportedEncodings.Add(Encoding.UTF8);
        SupportedEncodings.Add(Encoding.Unicode);
    }

    protected override bool CanReadType(Type type) =>
        type.Namespace == typeof(MultiStatus).Namespace;

    public override async Task<InputFormatterResult> ReadRequestBodyAsync(
        InputFormatterContext context,
        Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(encoding);

        var request = context.HttpContext.Request;
        if (request.ContentLength == 0)
        {
            return InputFormatterResult.NoValue();
        }

        using var reader = new StreamReader(request.Body, encoding, leaveOpen: true);
        var xml = await reader.ReadToEndAsync(context.HttpContext.RequestAborted);
        if (string.IsNullOrWhiteSpace(xml))
        {
            return InputFormatterResult.NoValue();
        }

        try
        {
            var method = typeof(WebDavXml)
                .GetMethod(nameof(WebDavXml.Deserialize), [typeof(string)])!
                .MakeGenericMethod(context.ModelType);
            var value = method.Invoke(null, [xml]);
            return InputFormatterResult.Success(value);
        }
        catch (Exception ex)
        {
            context.ModelState.TryAddModelError(context.ModelName, ex.Message);
            return InputFormatterResult.Failure();
        }
    }
}
