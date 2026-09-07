namespace SecondDimensionWatcherReDive.Framework.Plugin;

/// <summary>
/// Provides the two-phase, local-package installation boundary for JavaScript plugins.
/// A package is never evaluated by either operation; execution is only possible after
/// an explicit capability approval and a separate enable operation.
/// </summary>
public interface IJavaScriptPluginLoader
{
    Task<PluginPackagePreview> PreviewPackageAsync(
        Stream package,
        string fileName,
        CancellationToken cancellationToken);

    Task<PluginInstallResult> InstallPackageAsync(
        string previewToken,
        string expectedSha256,
        PluginCapabilities approvedCapabilities,
        CancellationToken cancellationToken);
}
