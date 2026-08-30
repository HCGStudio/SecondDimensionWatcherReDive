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

        pluginEvent.Register((param, _) =>
        {
            captured = param;
            return Task.CompletedTask;
        });

        var input = new FileDownloadStartParam(Guid.NewGuid(), "https://example.com", [], "hash123");
        await pluginEvent.InvokeAsync(input);

        Assert.IsNotNull(captured);
        Assert.AreEqual(input.Id, captured.Id);
        Assert.AreEqual(input.DownloadUrl, captured.DownloadUrl);
    }

    [TestMethod]
    public async Task Invoke_WithMultipleHandlers_CallsAllInOrder()
    {
        var pluginEvent = new PluginEvent<FileDownloadCompleteParam>();
        var callOrder = new List<int>();

        pluginEvent.Register((_, _) =>
        {
            callOrder.Add(1);
            return Task.CompletedTask;
        });
        pluginEvent.Register((_, _) =>
        {
            callOrder.Add(2);
            return Task.CompletedTask;
        });
        pluginEvent.Register((_, _) =>
        {
            callOrder.Add(3);
            return Task.CompletedTask;
        });

        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(Guid.NewGuid(), "/path", "local"));

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, callOrder);
    }

    [TestMethod]
    public async Task Invoke_WithNoHandlers_DoesNotThrow()
    {
        var pluginEvent = new PluginEvent<FileDownloadCompleteParam>();

        await pluginEvent.InvokeAsync(new FileDownloadCompleteParam(Guid.NewGuid(), "/path", "local"));

        // If we get here without exception, the test passes
    }

    [TestMethod]
    public async Task Invoke_WhenHandlerFailsOrTimesOut_ContinuesWithRemainingHandlers()
    {
        var errors = new List<Exception>();
        var pluginEvent = new PluginEvent<FileDownloadCompleteParam>(
            TimeSpan.FromMilliseconds(40),
            errors.Add);
        var calls = new List<int>();
        pluginEvent.Register((_, _) => throw new InvalidOperationException("broken"));
        pluginEvent.Register(async (_, _) => await Task.Delay(TimeSpan.FromSeconds(5)));
        pluginEvent.Register((_, _) =>
        {
            calls.Add(3);
            return Task.CompletedTask;
        });

        await pluginEvent.InvokeAsync(
            new FileDownloadCompleteParam(Guid.NewGuid(), "/path", "local"),
            CancellationToken.None);

        CollectionAssert.AreEqual(new[] { 3 }, calls);
        Assert.HasCount(2, errors);
    }

    [TestMethod]
    public async Task Invoke_WithSingleHandler_PropagatesCallerCancellation()
    {
        var errors = new List<Exception>();
        var pluginEvent = new PluginEvent<FileDownloadCompleteParam>(
            TimeSpan.FromSeconds(5),
            errors.Add);
        pluginEvent.Register(async (_, cancellationToken) =>
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => pluginEvent.InvokeAsync(
            new FileDownloadCompleteParam(Guid.NewGuid(), "/path", "local"), cancellation.Token));

        Assert.IsEmpty(errors);
    }
}
