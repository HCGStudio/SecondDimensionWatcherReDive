using SecondDimensionWatcherReDive.WebDav.Xml;

namespace SecondDimensionWatcherReDive.IntegrationTest.Helpers;

internal static class WebDavXmlAssertions
{
    public static async Task<MultiStatus> ReadMultiStatusAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        var multi = WebDavXml.Deserialize<MultiStatus>(stream)
                    ?? throw new InvalidOperationException("Response body is not a multistatus document");
        return multi;
    }

    public static DavResponse FindByHref(this MultiStatus multi, string href)
        => multi.Responses.FirstOrDefault(r => r.Href == href)
           ?? throw new InvalidOperationException($"No response with href {href}");
}
