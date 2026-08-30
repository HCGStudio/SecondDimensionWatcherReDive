using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Chat;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class ChatControllerStreamingTests
{
    [TestMethod]
    public async Task ProduceChatEventsAsync_ErrorWithFullChannel_DoesNotBlock()
    {
        var channel = Channel.CreateBounded<SseItem<string>>(1);
        Assert.IsTrue(channel.Writer.TryWrite(new SseItem<string>("buffered", "text_delta")));
        var controller = CreateController(new Mock<IChatRepository>(MockBehavior.Strict).Object);

        await controller.ProduceChatEventsAsync(
                new ThrowingEngine(),
                [],
                new ChatOptions(),
                Guid.NewGuid(),
                0,
                "question",
                false,
                null,
                channel.Writer,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.IsTrue(channel.Reader.TryRead(out var buffered));
        Assert.AreEqual("buffered", buffered.Data);
        await channel.Reader.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsFalse(channel.Reader.TryRead(out _));
    }

    [TestMethod]
    public async Task StreamChatEvents_EarlyReaderExit_CancelsAndJoinsProducer()
    {
        var conversationId = Guid.NewGuid();
        var repository = new Mock<IChatRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.AddMessagesAsync(
                conversationId,
                It.Is<IEnumerable<ChatMessageRecord>>(messages =>
                    messages.Any(message => message.Role == "assistant"
                                            && message.Content == "partial")),
                CancellationToken.None))
            .Returns(Task.CompletedTask);
        var engine = new CancellationAwareEngine();
        var controller = CreateController(repository.Object);
        var enumerator = controller.StreamChatEvents(
                engine,
                [],
                new ChatOptions(),
                conversationId,
                0,
                "question",
                false,
                null,
                CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.IsTrue(await enumerator.MoveNextAsync());
        await enumerator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        await engine.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(2));
        repository.VerifyAll();
    }

    private static ChatController CreateController(IChatRepository repository) =>
        new(
            repository,
            Mock.Of<IServiceScopeFactory>(),
            Mock.Of<IServiceProvider>(),
            NullLogger<ChatController>.Instance);

    private sealed class ThrowingEngine : IAIEngine
    {
        public Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AIModel>>([]);

        public async IAsyncEnumerable<IChatUpdate> ChatAsync(
            IReadOnlyList<IMessage> messages,
            ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (!cancellationToken.IsCancellationRequested)
                throw new InvalidOperationException("provider failed");
            cancellationToken.ThrowIfCancellationRequested();
            yield break;
        }
    }

    private sealed class CancellationAwareEngine : IAIEngine
    {
        public TaskCompletionSource CancellationObserved { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AIModel>>([]);

        public async IAsyncEnumerable<IChatUpdate> ChatAsync(
            IReadOnlyList<IMessage> messages,
            ChatOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new TextDelta("partial");
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }
    }
}
