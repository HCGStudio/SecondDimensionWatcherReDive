using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.Plugin;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal sealed class PluginPackageInspector(IOptions<PluginPlatformOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PluginPlatformOptions _options = options.Value;
    private readonly string _rootPath = Path.GetFullPath(options.Value.RootPath);
    private readonly SemaphoreSlim _stagingGate = new(1, 1);

    public async Task<InspectedPluginPackage> StageAndInspectAsync(
        Stream package,
        string fileName,
        CancellationToken cancellationToken)
    {
        if (!fileName.EndsWith(".sdwpkg", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Plugin packages must use the .sdwpkg or .zip extension.");

        await _stagingGate.WaitAsync(cancellationToken);
        try
        {
            var stagingPath = Path.Combine(_rootPath, "staging");
            Directory.CreateDirectory(stagingPath);
            RestrictDirectory(stagingPath);
            CleanupExpiredPreviews(stagingPath);
            var stagedPackages = Directory.EnumerateFiles(
                stagingPath, "*.sdwpkg", SearchOption.TopDirectoryOnly).ToArray();
            if (stagedPackages.Length >= _options.MaximumStagedPackages)
                throw new InvalidOperationException("The plugin preview staging limit has been reached.");
            var stagedBytes = stagedPackages.Sum(path => new FileInfo(path).Length);

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            var packagePath = Path.Combine(stagingPath, $"{token}.sdwpkg");
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            try
            {
                await using (var target = new FileStream(packagePath, FileMode.CreateNew, FileAccess.Write,
                                 FileShare.None, 64 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[64 * 1024];
                    long total = 0;
                    int read;
                    while ((read = await package.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        total = checked(total + read);
                        if (total > _options.MaximumPackageBytes)
                            throw new InvalidDataException(
                                $"Plugin package exceeds {_options.MaximumPackageBytes} bytes.");
                        if (stagedBytes + total > _options.MaximumStagedPackageBytes)
                            throw new InvalidOperationException("The plugin preview staging byte limit has been reached.");
                        hash.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                RestrictFile(packagePath);
                var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                return await InspectAsync(token, packagePath, sha256, cancellationToken);
            }
            catch
            {
                if (File.Exists(packagePath)) File.Delete(packagePath);
                throw;
            }
        }
        finally
        {
            _stagingGate.Release();
        }
    }

    public async Task<InspectedPluginPackage> InspectStagedAsync(
        string token,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token) || token.Length != 48 ||
            token.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Invalid preview token.");
        if (string.IsNullOrEmpty(expectedSha256) || expectedSha256.Length != 64 ||
            expectedSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Expected package checksum must be a 64-character SHA-256 value.");
        var path = Path.Combine(_rootPath, "staging", $"{token}.sdwpkg");
        if (!File.Exists(path)) throw new FileNotFoundException("Plugin preview expired or does not exist.");
        if (File.GetLastWriteTimeUtc(path).AddMinutes(_options.PreviewLifetimeMinutes) < DateTime.UtcNow)
        {
            File.Delete(path);
            throw new InvalidDataException("Plugin preview has expired.");
        }

        var actualSha256 = await ComputeSha256Async(path, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualSha256), Encoding.ASCII.GetBytes(expectedSha256.ToLowerInvariant())))
            throw new InvalidDataException("Package checksum no longer matches the approved preview.");

        return await InspectAsync(token, path, actualSha256, cancellationToken);
    }

    public async Task<string> ExtractAsync(
        InspectedPluginPackage package,
        CancellationToken cancellationToken)
    {
        var packagesRoot = Path.GetFullPath(Path.Combine(_rootPath, "packages"));
        var pluginRoot = GetContainedChildPath(packagesRoot, package.Manifest.Id, "plugin id");
        var finalPath = GetContainedChildPath(pluginRoot, package.Manifest.Version, "plugin version");
        var temporaryPath = GetContainedChildPath(
            pluginRoot,
            $".{package.Manifest.Version}.{Guid.NewGuid():N}.tmp",
            "temporary package path");
        Directory.CreateDirectory(pluginRoot);
        RestrictDirectory(pluginRoot);
        if (Directory.Exists(finalPath))
            throw new InvalidOperationException("This plugin version is already present on disk.");
        Directory.CreateDirectory(temporaryPath);
        RestrictDirectory(temporaryPath);

        try
        {
            using var archive = ZipFile.OpenRead(package.PackagePath);
            var extractedFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            long extractedBytes = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateArchiveEntry(entry);
                var destination = Path.GetFullPath(Path.Combine(temporaryPath, entry.FullName));
                if (!IsWithin(destination, temporaryPath))
                    throw new InvalidDataException($"Archive entry '{entry.FullName}' escapes the package root.");
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open();
                await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 64 * 1024, FileOptions.Asynchronous);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    extractedBytes = checked(extractedBytes + read);
                    if (extractedBytes > _options.MaximumExpandedBytes)
                        throw new InvalidDataException(
                            $"Expanded package exceeds {_options.MaximumExpandedBytes} bytes.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                RestrictFile(destination);
                if (!entry.FullName.Equals("manifest.json", StringComparison.Ordinal))
                {
                    extractedFiles[entry.FullName] =
                        Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                }
            }

            VerifyFileManifest(package.Manifest, extractedFiles);

            Directory.Move(temporaryPath, finalPath);
            return finalPath;
        }
        catch
        {
            if (Directory.Exists(temporaryPath)) Directory.Delete(temporaryPath, recursive: true);
            throw;
        }
    }

    public void Consume(InspectedPluginPackage package)
    {
        if (File.Exists(package.PackagePath)) File.Delete(package.PackagePath);
    }

    private async Task<InspectedPluginPackage> InspectAsync(
        string token,
        string packagePath,
        string sha256,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        if (archive.Entries.Count == 0 || archive.Entries.Count > _options.MaximumPackageFiles)
            throw new InvalidDataException($"Plugin package must contain 1-{_options.MaximumPackageFiles} files.");
        long expandedBytes = 0;
        if (archive.Entries.GroupBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
            throw new InvalidDataException("Plugin package contains duplicate archive paths.");
        foreach (var entry in archive.Entries)
        {
            ValidateArchiveEntry(entry);
            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > _options.MaximumExpandedBytes)
                throw new InvalidDataException($"Expanded package exceeds {_options.MaximumExpandedBytes} bytes.");
        }

        var manifestEntry = archive.GetEntry("manifest.json")
                            ?? throw new InvalidDataException("Plugin package is missing manifest.json.");
        if (manifestEntry.Length > 256 * 1024) throw new InvalidDataException("Plugin manifest is too large.");
        PluginManifest? manifest;
        await using (var stream = manifestEntry.Open())
            manifest = await JsonSerializer.DeserializeAsync<PluginManifest>(stream, JsonOptions, cancellationToken);
        if (manifest is null) throw new InvalidDataException("Plugin manifest is invalid.");
        manifest = Normalize(manifest);

        var validationErrors = PluginManifestValidator.Validate(manifest);
        if (validationErrors.Count > 0) throw new InvalidDataException(string.Join(" ", validationErrors));
        var actualFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name) &&
                                                             !entry.FullName.Equals("manifest.json", StringComparison.Ordinal)))
        {
            actualFiles[entry.FullName] = await ComputeSha256Async(entry, cancellationToken);
        }
        VerifyFileManifest(manifest, actualFiles);

        var (trusted, status, publisherFingerprint) = VerifySignature(manifest);
        return new InspectedPluginPackage(
            token,
            packagePath,
            sha256,
            manifest,
            trusted,
            status,
            publisherFingerprint,
            new DateTimeOffset(File.GetLastWriteTimeUtc(packagePath).AddMinutes(_options.PreviewLifetimeMinutes),
                TimeSpan.Zero));
    }

    private (bool IsTrusted, string Status, string? PublisherFingerprint) VerifySignature(PluginManifest manifest)
    {
        if (manifest.Signature is null) return (false, "Package is unsigned.", null);
        if (!_options.TrustedPublisherPublicKeys.TryGetValue(manifest.Signature.Publisher, out var publicKey))
            return (false, $"Publisher '{manifest.Signature.Publisher}' is not trusted by this deployment.", null);
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);
            if (rsa.KeySize < 2048)
                return (false, "Trusted publisher RSA keys must be at least 2048 bits.", null);
            var payload = PluginSignaturePayload.Create(manifest);
            var signature = Convert.FromBase64String(manifest.Signature.Value);
            var valid = rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var fingerprint = Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()))
                .ToLowerInvariant();
            return valid
                ? (true, $"Signature verified for trusted publisher '{manifest.Signature.Publisher}'.", fingerprint)
                : (false, "Package signature is invalid.", null);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return (false, $"Package signature could not be verified: {exception.Message}", null);
        }
    }

    private static PluginManifest Normalize(PluginManifest manifest)
    {
        var capabilities = manifest.Capabilities ?? new PluginCapabilities();
        capabilities = capabilities with
        {
            NetworkDomains = capabilities.NetworkDomains?.Where(value => value is not null).ToArray() ?? [],
            FileRoots = capabilities.FileRoots?.Where(value => value is not null).ToArray() ?? []
        };
        return manifest with
        {
            Id = manifest.Id ?? string.Empty,
            Name = manifest.Name ?? string.Empty,
            Version = manifest.Version ?? string.Empty,
            ApiVersion = manifest.ApiVersion ?? string.Empty,
            EntryPoint = manifest.EntryPoint ?? string.Empty,
            Dependencies = manifest.Dependencies?.Where(value => value is not null).Select(value =>
                new PluginDependency(value.Id ?? string.Empty, value.MinimumVersion ?? string.Empty)).ToArray() ?? [],
            Capabilities = capabilities,
            Platforms = manifest.Platforms?.Where(value => value is not null).ToArray() ?? [],
            Providers = manifest.Providers?.Where(value => value is not null).Select(value => value with
            {
                Kind = value.Kind ?? string.Empty,
                Name = value.Name ?? string.Empty,
                Handlers = value.Handlers ?? new Dictionary<string, string>()
            }).ToArray() ?? [],
            Integrity = manifest.Integrity is null
                ? null
                : new PluginIntegrity
                {
                    Files = manifest.Integrity.Files?
                        .Where(value => value.Key is not null && value.Value is not null)
                        .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal)
                        ?? new Dictionary<string, string>(StringComparer.Ordinal)
                },
            Signature = manifest.Signature is null
                ? null
                : new PluginSignature(
                    manifest.Signature.Publisher ?? string.Empty,
                    manifest.Signature.Algorithm ?? string.Empty,
                    manifest.Signature.Value ?? string.Empty),
            DataMigration = manifest.DataMigration is null
                ? null
                : manifest.DataMigration with { Strategy = manifest.DataMigration.Strategy ?? string.Empty }
        };
    }

    private void CleanupExpiredPreviews(string stagingPath)
    {
        foreach (var path in Directory.EnumerateFiles(stagingPath, "*.sdwpkg"))
        {
            if (File.GetLastWriteTimeUtc(path).AddMinutes(_options.PreviewLifetimeMinutes) < DateTime.UtcNow)
                File.Delete(path);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256Async(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static void ValidateArchiveEntry(ZipArchiveEntry entry)
    {
        var path = string.IsNullOrEmpty(entry.Name)
            ? entry.FullName.TrimEnd('/')
            : entry.FullName;
        if (!PluginManifestValidator.IsSafeArchivePath(path))
            throw new InvalidDataException($"Archive entry '{entry.FullName}' is unsafe.");
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixFileType == 0xA000)
            throw new InvalidDataException($"Archive entry '{entry.FullName}' is a symbolic link.");
    }

    private static void VerifyFileManifest(
        PluginManifest manifest,
        IReadOnlyDictionary<string, string> actualFiles)
    {
        var expectedFiles = manifest.Integrity?.Files
                            ?? throw new InvalidDataException("Plugin integrity metadata is missing.");
        var missing = expectedFiles.Keys.Except(actualFiles.Keys, StringComparer.Ordinal).Order().ToArray();
        var unlisted = actualFiles.Keys.Except(expectedFiles.Keys, StringComparer.Ordinal).Order().ToArray();
        if (missing.Length > 0 || unlisted.Length > 0)
        {
            throw new InvalidDataException(
                $"Package file list does not match signed integrity metadata. Missing: {FormatPaths(missing)}; unlisted: {FormatPaths(unlisted)}.");
        }

        foreach (var actual in actualFiles)
        {
            if (!actual.Value.Equals(expectedFiles[actual.Key], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Package file '{actual.Key}' failed its integrity check.");
        }
    }

    private static string FormatPaths(IReadOnlyList<string> paths)
        => paths.Count == 0 ? "none" : string.Join(", ", paths);

    private static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathFullyQualified(relative);
    }

    private static string GetContainedChildPath(string root, string child, string fieldName)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, child));
        if (!IsWithin(candidate, fullRoot) ||
            string.Equals(candidate, fullRoot, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
            throw new InvalidDataException($"The {fieldName} escapes its package directory.");
        return candidate;
    }

    private static void RestrictDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

internal sealed record InspectedPluginPackage(
    string Token,
    string PackagePath,
    string PackageSha256,
    PluginManifest Manifest,
    bool IsSignatureTrusted,
    string SignatureStatus,
    string? PublisherFingerprint,
    DateTimeOffset ExpiresAt);
