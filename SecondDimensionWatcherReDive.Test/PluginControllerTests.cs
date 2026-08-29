using Microsoft.AspNetCore.Http;
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
