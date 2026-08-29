using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal static partial class PluginManifestValidator
{
    [GeneratedRegex("^[a-z](?:[a-z0-9-]*[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex HandlerNamePattern();

    private const int MaximumVersionLength = 128;

    public static bool IsValidId(string id)
        => !string.IsNullOrWhiteSpace(id) && id.Length is >= 3 and <= 64 && IdPattern().IsMatch(id);
    public static bool IsValidHandlerName(string name)
        => !string.IsNullOrWhiteSpace(name) && HandlerNamePattern().IsMatch(name);

    public static IReadOnlyList<string> Validate(PluginManifest manifest)
    {
        var errors = new List<string>();
        if (!IsValidId(manifest.Id))
            errors.Add("Plugin id must be 3-64 lowercase ASCII characters in non-empty dot-separated segments.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 128 ||
            manifest.Name.Any(char.IsControl))
            errors.Add("Plugin name is required, must be at most 128 characters, and cannot contain control characters.");
        if (manifest.Description is { } description &&
            (description.Length > 2_048 || description.Any(IsDisallowedDescriptionCharacter)))
            errors.Add("Plugin description must be at most 2048 characters and cannot contain unsafe control characters.");
        if (!TryParseVersion(manifest.Version, out _)) errors.Add("Plugin version must be a valid semantic version.");
        if (!TryParseApiVersion(manifest.ApiVersion, out _)) errors.Add("API version must be a valid API version.");
        if (!IsSafeRelativePath(manifest.EntryPoint) || !manifest.EntryPoint.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            errors.Add("Entry point must be a relative JavaScript file path.");
        if (manifest.DataVersion < 1) errors.Add("Data version must be at least 1.");

        foreach (var dependency in manifest.Dependencies)
        {
            if (!IsValidId(dependency.Id)) errors.Add($"Dependency id '{dependency.Id}' is invalid.");
            if (!TryParseVersion(dependency.MinimumVersion, out _))
                errors.Add($"Dependency '{dependency.Id}' has an invalid minimum version.");
        }

        if (manifest.Dependencies.GroupBy(x => x.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
            errors.Add("Dependencies must not contain duplicate ids.");
        if (manifest.Providers.GroupBy(x => $"{x.Kind}:{x.Name}", StringComparer.Ordinal).Any(group => group.Count() > 1))
            errors.Add("Provider declarations must have unique kind/name pairs.");

        foreach (var provider in manifest.Providers)
        {
            if (provider.Kind is not ("notification" or "storage"))
                errors.Add($"Provider '{provider.Name}' has unsupported kind '{provider.Kind}'.");
            if (string.IsNullOrWhiteSpace(provider.Name) || provider.Handlers.Count == 0)
                errors.Add("Provider declarations require a name and at least one handler.");
            if (!IsValidIdentifier(provider.Name))
                errors.Add($"Provider '{provider.Name}' name must be a 1-64 character ASCII identifier.");
            if (provider.Handlers.Keys.Any(operation => !IsValidIdentifier(operation)))
                errors.Add($"Provider '{provider.Name}' contains an invalid operation name.");
            if (provider.Handlers.Values.Any(handler => !IsValidHandlerName(handler)))
                errors.Add($"Provider '{provider.Name}' contains an invalid handler name.");
            if (provider.Kind == "notification" && !provider.Handlers.ContainsKey("send"))
                errors.Add($"Notification provider '{provider.Name}' requires a send handler.");
            if (provider.Kind == "storage" &&
                new[] { "exists", "info", "read", "list" }.Any(operation => !provider.Handlers.ContainsKey(operation)))
                errors.Add($"Storage provider '{provider.Name}' requires exists, info, read and list handlers.");
        }

        if (manifest.Providers.Any(provider => provider.Kind == "storage") && !manifest.Capabilities.StorageAccess)
            errors.Add("Storage providers require the storageAccess capability.");
        if (manifest.Providers.Any(provider => provider.Kind == "notification") &&
            !manifest.Capabilities.Notifications)
            errors.Add("Notification providers require the notifications capability.");

        foreach (var domain in manifest.Capabilities.NetworkDomains)
        {
            if (!IsValidDomainPattern(domain)) errors.Add($"Network domain '{domain}' is invalid.");
        }

        foreach (var root in manifest.Capabilities.FileRoots)
        {
            if (!Path.IsPathFullyQualified(root)) errors.Add($"File root '{root}' must be absolute.");
        }

        if (manifest.Integrity?.Files is not { Count: > 0 } files)
        {
            errors.Add("Integrity metadata must contain a SHA-256 digest for every package file.");
        }
        else
        {
            foreach (var file in files)
            {
                if (!IsSafeArchivePath(file.Key) ||
                    file.Key.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Integrity path '{file.Key}' is invalid.");
                if (!IsSha256(file.Value))
                    errors.Add($"Integrity digest for '{file.Key}' must be a valid SHA-256 value.");
            }
            if (!files.ContainsKey(manifest.EntryPoint.Replace('\\', '/')))
                errors.Add("Integrity metadata must include the entry point.");
        }
        if (manifest.Signature is { Algorithm: not "RSA-SHA256" })
            errors.Add("Only RSA-SHA256 signatures are supported.");
        if (manifest.Signature is { } signature && !IsValidIdentifier(signature.Publisher))
            errors.Add("Signature publisher must be a 1-64 character ASCII identifier.");
        if (manifest.DataMigration is { } migration &&
            !string.Equals(migration.Strategy, "preserve", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(migration.Strategy, "reset", StringComparison.OrdinalIgnoreCase))
            errors.Add("Data migration strategy must be 'preserve' or 'reset'.");
        if (manifest.DataMigration?.Description is { } migrationDescription &&
            (migrationDescription.Length > 2_048 ||
             migrationDescription.Any(IsDisallowedDescriptionCharacter)))
            errors.Add("Data migration description must be at most 2048 characters and cannot contain unsafe control characters.");

        return errors;
    }

    public static IReadOnlyList<string> GetCompatibilityErrors(
        PluginManifest manifest,
        IReadOnlyDictionary<string, PluginCatalogEntryView> installed)
    {
        var errors = new List<string>();
        if (!TryParseApiVersion(manifest.ApiVersion, out var requested) ||
            !TryParseApiVersion(PluginApi.CurrentVersion, out var current) ||
            requested.Major != current.Major || requested > current)
        {
            errors.Add($"Plugin API {manifest.ApiVersion} is incompatible with host API {PluginApi.CurrentVersion}.");
        }

        var platform = $"{GetOs()}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}";
        if (manifest.Platforms.Count > 0 &&
            !manifest.Platforms.Contains("any", StringComparer.OrdinalIgnoreCase) &&
            !manifest.Platforms.Contains(platform, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Plugin does not support host platform '{platform}'.");
        }
        if (OperatingSystem.IsWindows() &&
            (manifest.Capabilities.StorageAccess || manifest.Capabilities.FileRoots.Count > 0 ||
             manifest.Providers.Any(provider => provider.Kind == "storage")))
        {
            errors.Add("Plugin file and storage capabilities are not supported on Windows in API 1.0.");
        }

        foreach (var dependency in manifest.Dependencies)
        {
            if (!installed.TryGetValue(dependency.Id, out var installedDependency))
            {
                errors.Add($"Required dependency '{dependency.Id}' is not installed.");
                continue;
            }

            if (!installedDependency.IsEnabled)
                errors.Add($"Required dependency '{dependency.Id}' is disabled.");
            if (!TryParseVersion(installedDependency.Version, out var actual) ||
                !TryParseVersion(dependency.MinimumVersion, out var minimum) || actual < minimum)
            {
                errors.Add($"Dependency '{dependency.Id}' requires >= {dependency.MinimumVersion}; installed version is {installedDependency.Version}.");
            }
        }

        return errors;
    }

    public static bool CapabilitiesEqual(PluginCapabilities left, PluginCapabilities right)
        => left.Notifications == right.Notifications &&
           left.DownloadControl == right.DownloadControl &&
           left.StorageAccess == right.StorageAccess &&
           left.BackgroundTasks == right.BackgroundTasks &&
           SetEqual(left.NetworkDomains, right.NetworkDomains, StringComparer.OrdinalIgnoreCase) &&
           SetEqual(left.FileRoots.Select(Path.GetFullPath), right.FileRoots.Select(Path.GetFullPath), PathComparer);

    public static bool TryParseVersion(string value, out SemanticVersion version)
        => TryParseSemanticVersion(value, requirePatch: true, out version);

    private static bool TryParseApiVersion(string value, out SemanticVersion version)
        => TryParseSemanticVersion(value, requirePatch: false, out version);

    private static bool TryParseSemanticVersion(
        string value,
        bool requirePatch,
        out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(value) || value.Length > MaximumVersionLength ||
            value.Any(character => character > 0x7f))
            return false;

        var buildSeparator = value.IndexOf('+');
        var withoutBuild = buildSeparator < 0 ? value : value[..buildSeparator];
        if (buildSeparator >= 0)
        {
            var build = value[(buildSeparator + 1)..];
            if (!AreValidIdentifiers(build, rejectNumericLeadingZero: false)) return false;
        }

        var prereleaseSeparator = withoutBuild.IndexOf('-');
        var core = prereleaseSeparator < 0 ? withoutBuild : withoutBuild[..prereleaseSeparator];
        var prerelease = prereleaseSeparator < 0 ? [] : withoutBuild[(prereleaseSeparator + 1)..].Split('.');
        if (prereleaseSeparator >= 0 && !AreValidIdentifiers(
                withoutBuild[(prereleaseSeparator + 1)..], rejectNumericLeadingZero: true))
            return false;

        var components = core.Split('.');
        if (components.Length != 3 && (requirePatch || components.Length != 2)) return false;
        var patch = 0;
        if (!TryParseNumericComponent(components[0], out var major) ||
            !TryParseNumericComponent(components[1], out var minor) ||
            (components.Length == 3 && !TryParseNumericComponent(components[2], out patch)))
            return false;

        version = new SemanticVersion(major, minor, patch, prerelease);
        return true;
    }

    private static bool TryParseNumericComponent(string value, out int component)
    {
        component = 0;
        return value.Length > 0 && (value.Length == 1 || value[0] != '0') &&
               value.All(IsAsciiDigit) &&
               int.TryParse(value, System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out component);
    }

    private static bool AreValidIdentifiers(string value, bool rejectNumericLeadingZero)
    {
        var identifiers = value.Split('.');
        return identifiers.All(identifier =>
            identifier.Length > 0 &&
            identifier.All(character => IsAsciiLetterOrDigit(character) || character == '-') &&
            (!rejectNumericLeadingZero || !identifier.All(IsAsciiDigit) ||
             identifier.Length == 1 || identifier[0] != '0'));
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    private static bool IsAsciiLetterOrDigit(char value)
        => IsAsciiDigit(value) || value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsValidIdentifier(string value)
        => !string.IsNullOrEmpty(value) && HandlerNamePattern().IsMatch(value);

    private static bool IsDisallowedDescriptionCharacter(char value)
        => char.IsControl(value) && value is not '\r' and not '\n' and not '\t';

    public static bool IsSafeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathFullyQualified(value)) return false;
        var normalized = value.Replace('\\', '/');
        return normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..");
    }

    public static bool IsSafeArchivePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.StartsWith('/') ||
            value.EndsWith('/') || value.Contains("//", StringComparison.Ordinal) ||
            value.Contains(':'))
            return false;
        return IsSafeRelativePath(value);
    }

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsValidDomainPattern(string value)
    {
        var domain = value.StartsWith("*.", StringComparison.Ordinal) ? value[2..] : value;
        return Uri.CheckHostName(domain) == UriHostNameType.Dns &&
               !domain.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetEqual(IEnumerable<string> left, IEnumerable<string> right, StringComparer comparer)
        => new HashSet<string>(left, comparer).SetEquals(right);

    private static string GetOs()
        => OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

internal readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    IReadOnlyList<string> Prerelease) : IComparable<SemanticVersion>
{
    public int CompareTo(SemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        if (Prerelease.Count == 0) return other.Prerelease.Count == 0 ? 0 : 1;
        if (other.Prerelease.Count == 0) return -1;
        for (var index = 0; index < Math.Min(Prerelease.Count, other.Prerelease.Count); index++)
        {
            var left = Prerelease[index];
            var right = other.Prerelease[index];
            var leftNumeric = left.All(character => character is >= '0' and <= '9');
            var rightNumeric = right.All(character => character is >= '0' and <= '9');
            int comparison;
            if (leftNumeric && rightNumeric)
            {
                comparison = left.Length.CompareTo(right.Length);
                if (comparison == 0) comparison = string.CompareOrdinal(left, right);
            }
            else if (leftNumeric != rightNumeric)
            {
                comparison = leftNumeric ? -1 : 1;
            }
            else
            {
                comparison = string.CompareOrdinal(left, right);
            }

            if (comparison != 0) return comparison;
        }

        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
}

internal sealed record PluginCatalogEntryView(string Version, bool IsEnabled);
