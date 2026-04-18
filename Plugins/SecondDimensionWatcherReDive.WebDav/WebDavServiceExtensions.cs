using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.WebDav.Formatters;

namespace SecondDimensionWatcherReDive.WebDav;

public static class WebDavServiceExtensions
{
    public static IMvcBuilder AddWebDav(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddMvcOptions(options =>
        {
            if (!options.InputFormatters.OfType<WebDavXmlInputFormatter>().Any())
            {
                options.InputFormatters.Insert(0, new WebDavXmlInputFormatter());
            }

            if (!options.OutputFormatters.OfType<WebDavXmlOutputFormatter>().Any())
            {
                options.OutputFormatters.Insert(0, new WebDavXmlOutputFormatter());
            }
        });
        return builder;
    }
}
