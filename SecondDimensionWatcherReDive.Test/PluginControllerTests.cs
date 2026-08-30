using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SecondDimensionWatcherReDive.Controllers;
using SecondDimensionWatcherReDive.Framework.Plugin;
using SecondDimensionWatcherReDive.PluginPlatform;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class PluginControllerTests
{
    [TestMethod]
    public void Preview_AllowsTheConfiguredPackageRangeAtTheMultipartBoundary()
    {
        var method = typeof(PluginController).GetMethod(nameof(PluginController.Preview));
        Assert.IsNotNull(method);
        var requestLimit = method.GetCustomAttribute<RequestSizeLimitAttribute>();
        var formLimit = method.GetCustomAttribute<RequestFormLimitsAttribute>();

        Assert.IsNotNull(requestLimit);
        Assert.IsNotNull(formLimit);
        Assert.AreEqual(
            PluginPlatformOptions.MaximumUploadRequestBytes,
            ((IRequestSizeLimitMetadata)requestLimit).MaxRequestBodySize);
        Assert.AreEqual(PluginPlatformOptions.MaximumAllowedPackageBytes, formLimit.MultipartBodyLengthLimit);
        Assert.IsGreaterThan(
            PluginPlatformOptions.MaximumAllowedPackageBytes,
            PluginPlatformOptions.MaximumUploadRequestBytes,
            "The request envelope must leave room for multipart framing.");
    }

    [TestMethod]
    public void PluginPlatform_WhenRootIsMissing_FallsBackBesideThePasswordFile()
    {
        var passwordFile = Path.Combine(Path.GetTempPath(), "sdw-app-data", "password.json");
        var defaultRoot = PluginPlatformOptions.GetDefaultRootPath(passwordFile);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddPluginPlatform(configuration, defaultRoot);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PluginPlatformOptions>>().Value;

        Assert.AreEqual(Path.GetFullPath(defaultRoot), options.RootPath);
    }

    [TestMethod]
    public void PluginPlatform_WhenRootIsConfigured_PreservesTheConfiguredPath()
    {
        var configuredRoot = Path.Combine(Path.GetTempPath(), "sdw-configured-plugins");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{PluginPlatformOptions.SectionName}:RootPath"] = configuredRoot
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPluginPlatform(configuration, Path.Combine(Path.GetTempPath(), "unused-default"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<PluginPlatformOptions>>().Value;

        Assert.AreEqual(configuredRoot, options.RootPath);
    }

    [TestMethod]
    public async Task Preview_WhenStagingCapacityIsReached_ReturnsConflict()
    {
        var loader = new Mock<IJavaScriptPluginLoader>();
        loader.Setup(value => value.PreviewPackageAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("The plugin preview staging limit has been reached."));
        var controller = new PluginController(null!, loader.Object);
        await using var content = new MemoryStream([1]);
        var package = new FormFile(content, 0, content.Length, "package", "test.sdwpkg");

        var result = await controller.Preview(package, CancellationToken.None);

        var conflict = Assert.IsInstanceOfType<ConflictObjectResult>(result.Result);
        Assert.AreEqual(StatusCodes.Status409Conflict, conflict.StatusCode);
        var error = Assert.IsInstanceOfType<Controllers.External.PluginOperationError>(conflict.Value);
        Assert.AreEqual("plugin_preview_capacity_reached", error.Code);
    }
}
