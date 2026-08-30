using System.Security.Claims;
using SecondDimensionWatcherReDive.Framework.Authorization;

namespace SecondDimensionWatcherReDive.Auth;

internal static class DevicePathScope
{
    public static string GetVirtualRoot(ClaimsPrincipal principal) =>
        TryNormalizeAbsolutePath(
            principal.FindFirst(IdentityClaimTypes.VirtualRoot)?.Value,
            out var root)
            ? root
            : "/";

    public static bool TryNormalizeAbsolutePath(string? raw, out string normalized)
    {
        if (string.IsNullOrEmpty(raw))
        {
            normalized = "/";
            return true;
        }

        if (!raw.StartsWith("/", StringComparison.Ordinal)
            || raw.Contains('\\', StringComparison.Ordinal)
            || raw.Any(char.IsControl))
        {
            normalized = string.Empty;
            return false;
        }

        var segments = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".."))
        {
            normalized = string.Empty;
            return false;
        }

        normalized = segments.Length == 0 ? "/" : "/" + string.Join('/', segments);
        return true;
    }

    public static bool TryMapPublicToInternal(
        string? publicPath,
        string virtualRoot,
        out string normalizedPublicPath,
        out string internalPath)
    {
        if (!TryNormalizeAbsolutePath(publicPath, out normalizedPublicPath)
            || !TryNormalizeAbsolutePath(virtualRoot, out var root))
        {
            internalPath = string.Empty;
            return false;
        }

        internalPath = root switch
        {
            "/" => normalizedPublicPath,
            _ when normalizedPublicPath == "/" => root,
            _ => root + normalizedPublicPath
        };
        return true;
    }

    public static bool TryMapInternalToPublic(
        string internalPath,
        string virtualRoot,
        out string publicPath)
    {
        publicPath = string.Empty;
        if (!TryNormalizeAbsolutePath(internalPath, out var normalizedInternal)
            || !TryNormalizeAbsolutePath(virtualRoot, out var root))
            return false;

        if (root == "/")
        {
            publicPath = normalizedInternal;
            return true;
        }

        if (normalizedInternal == root)
        {
            publicPath = "/";
            return true;
        }

        // Include the slash in the prefix: /Anime is a parent of /Anime/file,
        // but never of /Anime2/file.
        var rootedPrefix = root + "/";
        if (!normalizedInternal.StartsWith(rootedPrefix, StringComparison.Ordinal))
            return false;

        publicPath = normalizedInternal[root.Length..];
        return true;
    }
}
