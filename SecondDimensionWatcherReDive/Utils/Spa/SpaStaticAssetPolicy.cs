using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace SecondDimensionWatcherReDive.Utils.Spa;

internal static partial class SpaStaticAssetPolicy
{
    internal const string ImmutableCacheControl = "public,max-age=31536000,immutable";
    internal const string RevalidateCacheControl = "no-cache";
    internal const string ShortCacheControl = "public,max-age=3600";

    [GeneratedRegex(@"\.[0-9a-f]{8,}\.[^./]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintedAssetRegex();

    internal static string CacheControlFor(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".html", StringComparison.OrdinalIgnoreCase))
            return RevalidateCacheControl;

        return FingerprintedAssetRegex().IsMatch(path)
            ? ImmutableCacheControl
            : ShortCacheControl;
    }

    internal static void Apply(StaticFileResponseContext context)
    {
        context.Context.Response.Headers.CacheControl = CacheControlFor(context.File.Name);
    }
}
