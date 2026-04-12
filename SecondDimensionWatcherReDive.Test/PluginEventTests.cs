using SecondDimensionWatcherReDive.Framework.PluginParams;
using SecondDimensionWatcherReDive.Plugin;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class PluginEventTests
{
    [TestMethod]
    public async Task Invoke_WithSingleHandler_CallsHandler()
    {
        var pluginEvent = new PluginEvent<FileDownloadStartParam>();
        FileDownloadStartParam? captured = null;

        pluginEvent.Register(param =>
        {
            captured = param;
            return Task.CompletedTask;
        });

        var input = new FileDownloadStartParam(Guid.NewGuid(), "https://example.com", [], "hash123");
        await pluginEvent.Invoke(input);

        Assert.IsNotNull(captured);
        Assert.AreEqual(input.Id, captured.Id);
        Assert.AreEqual(input.DownloadUrl, captured.DownloadUrl);
    }

    [TestMethod]
    public async Task Invoke_WithMultipleHandlers_CallsAllInOrder()
    {
        var pluginEvent = new PluginEvent<FileDownloadCompleteParam>();
        var callOrder = new List<int>();

        pluginEvent.Register(_ =>
        {
            callOrder.Add(1);
            return Task.CompletedTask;
        });
        pluginEvent.Register(_ =>
        {
            callOrder.Add(2);
            return Task.CompletedTask;
        });
        pluginEvent.Register(_ =>
        {
            callOrder.Add(3);
            return Task.CompletedTask;
        });

        await pluginEvent.Invoke(new FileDownloadCompleteParam(Guid.NewGuid(), "/path", "local"));

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, callOrder);
    }

    [TestMethod]
    public async Task Invoke_WithNoHandlers_DoesNotThrow()
    {
        var pluginEvent = new PluginEvent<FileDownloadCompleteParam>();

        await pluginEvent.Invoke(new FileDownloadCompleteParam(Guid.NewGuid(), "/path", "local"));

        // If we get here without exception, the test passes
    }
}
