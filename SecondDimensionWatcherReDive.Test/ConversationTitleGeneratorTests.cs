using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Chat;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class ConversationTitleGeneratorTests
{
    private static readonly Guid ProfileId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    private static IServiceProvider ServicesWith(IAIEngine? engine)
    {
        var services = new ServiceCollection();
        if (engine is not null) services.AddSingleton(engine);
        return services.BuildServiceProvider();
    }

    private static Mock<IAIEngine> EngineReturning(string text)
    {
        var mock = new Mock<IAIEngine>();
        mock.Setup(e => e.ChatAsync(It.IsAny<IReadOnlyList<IMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Stream(text));
        return mock;

        static async IAsyncEnumerable<IChatUpdate> Stream(string text)
        {
            yield return new TextDelta(text);
            yield return new Finished("stop");
            await Task.CompletedTask;
        }
    }

    [TestMethod]
    public void Sanitize_StripsQuotesMarkdownAndPunctuation()
    {
        Assert.AreEqual("Hello World", ConversationTitleGenerator.Sanitize("\"Hello World\""));
        Assert.AreEqual("Hello World", ConversationTitleGenerator.Sanitize("**Hello World**"));
        Assert.AreEqual("Hello World", ConversationTitleGenerator.Sanitize("Hello World."));
        Assert.AreEqual("Hello World", ConversationTitleGenerator.Sanitize("- Hello World"));
        Assert.AreEqual("Hello World", ConversationTitleGenerator.Sanitize("1. Hello World"));
        Assert.AreEqual("订阅新番", ConversationTitleGenerator.Sanitize("「订阅新番」"));
        Assert.AreEqual("第一行", ConversationTitleGenerator.Sanitize("第一行\n第二行"));
    }

    [TestMethod]
    public void Sanitize_ReturnsNullForEmpty()
    {
        Assert.IsNull(ConversationTitleGenerator.Sanitize(""));
        Assert.IsNull(ConversationTitleGenerator.Sanitize("   "));
        Assert.IsNull(ConversationTitleGenerator.Sanitize("\"\""));
    }

    [TestMethod]
    public void Sanitize_TruncatesOverlong()
    {
        var s = ConversationTitleGenerator.Sanitize(new string('A', 200));
        Assert.IsNotNull(s);
        Assert.IsTrue(s!.Length <= 60);
    }

    [TestMethod]
    public void IsAutoTitleEligible_OnlyForNullOrWhitespace()
    {
        Assert.IsTrue(ConversationTitleGenerator.IsAutoTitleEligible(null));
        Assert.IsTrue(ConversationTitleGenerator.IsAutoTitleEligible(""));
        Assert.IsTrue(ConversationTitleGenerator.IsAutoTitleEligible("   "));
        Assert.IsFalse(ConversationTitleGenerator.IsAutoTitleEligible("My title"));
    }

    [TestMethod]
    public async Task GenerateAsync_ReturnsNullWhenAiEngineMissing()
    {
        var repo = new Mock<IChatRepository>();
        var gen = new ConversationTitleGenerator(ServicesWith(null), repo.Object, NullLogger<ConversationTitleGenerator>.Instance);

        var result = await gen.GenerateAsync("hi", "hello", null, CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GenerateAsync_SanitizesEngineOutput()
    {
        var engine = EngineReturning("\"Trip planner\"");
        var gen = new ConversationTitleGenerator(ServicesWith(engine.Object),
            Mock.Of<IChatRepository>(), NullLogger<ConversationTitleGenerator>.Instance);

        var result = await gen.GenerateAsync("plan a trip", "sure!", null, CancellationToken.None);

        Assert.AreEqual("Trip planner", result);
    }

    [TestMethod]
    public async Task GenerateAsync_DisablesToolsAndPassesModel()
    {
        ChatOptions? captured = null;
        var engine = new Mock<IAIEngine>();
        engine.Setup(e => e.ChatAsync(It.IsAny<IReadOnlyList<IMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<IMessage>, ChatOptions, CancellationToken>((_, opts, _) => captured = opts)
            .Returns(Stream());

        var gen = new ConversationTitleGenerator(ServicesWith(engine.Object),
            Mock.Of<IChatRepository>(), NullLogger<ConversationTitleGenerator>.Instance);

        await gen.GenerateAsync("u", "a", "gpt-4o-mini", CancellationToken.None);

        Assert.IsNotNull(captured);
        Assert.AreEqual("gpt-4o-mini", captured!.Model);
        Assert.IsNull(captured.ToolExecutor);
        Assert.AreEqual(1, captured.MaxToolRounds);

        static async IAsyncEnumerable<IChatUpdate> Stream()
        {
            yield return new TextDelta("Title");
            await Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task TryAutoTitle_SavesTitleWhenEligible()
    {
        var convId = Guid.NewGuid();
        var engine = EngineReturning("Anime subscription");
        var repo = new Mock<IChatRepository>();
        repo.Setup(r => r.GetConversationWithMessagesAsync(
                convId, ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatConversationDetail(convId, null, DateTimeOffset.Now, DateTimeOffset.Now, []));

        var gen = new ConversationTitleGenerator(ServicesWith(engine.Object), repo.Object,
            NullLogger<ConversationTitleGenerator>.Instance);

        await gen.TryAutoTitleAsync(
            convId, ProfileId, "请订阅新番", "好的", null, CancellationToken.None);

        repo.Verify(r => r.UpdateConversationTitleAsync(
                convId, ProfileId, "Anime subscription", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task TryAutoTitle_SkipsWhenTitleAlreadySet()
    {
        var convId = Guid.NewGuid();
        var engine = EngineReturning("Generated");
        var repo = new Mock<IChatRepository>();
        repo.Setup(r => r.GetConversationWithMessagesAsync(
                convId, ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatConversationDetail(convId, "User chose this", DateTimeOffset.Now, DateTimeOffset.Now, []));

        var gen = new ConversationTitleGenerator(ServicesWith(engine.Object), repo.Object,
            NullLogger<ConversationTitleGenerator>.Instance);

        await gen.TryAutoTitleAsync(convId, ProfileId, "u", "a", null, CancellationToken.None);

        repo.Verify(r => r.UpdateConversationTitleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task TryAutoTitle_DoesNotSaveOnEmptyOutput()
    {
        var convId = Guid.NewGuid();
        var engine = EngineReturning("   ");
        var repo = new Mock<IChatRepository>();
        repo.Setup(r => r.GetConversationWithMessagesAsync(
                convId, ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatConversationDetail(convId, null, DateTimeOffset.Now, DateTimeOffset.Now, []));

        var gen = new ConversationTitleGenerator(ServicesWith(engine.Object), repo.Object,
            NullLogger<ConversationTitleGenerator>.Instance);

        await gen.TryAutoTitleAsync(convId, ProfileId, "u", "a", null, CancellationToken.None);

        repo.Verify(r => r.UpdateConversationTitleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task TryAutoTitle_SwallowsEngineExceptions()
    {
        var convId = Guid.NewGuid();
        var engine = new Mock<IAIEngine>();
        engine.Setup(e => e.ChatAsync(It.IsAny<IReadOnlyList<IMessage>>(), It.IsAny<ChatOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Throwing());

        var repo = new Mock<IChatRepository>();
        repo.Setup(r => r.GetConversationWithMessagesAsync(
                convId, ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatConversationDetail(convId, null, DateTimeOffset.Now, DateTimeOffset.Now, []));

        var gen = new ConversationTitleGenerator(ServicesWith(engine.Object), repo.Object,
            NullLogger<ConversationTitleGenerator>.Instance);

        await gen.TryAutoTitleAsync(convId, ProfileId, "u", "a", null, CancellationToken.None);

        repo.Verify(r => r.UpdateConversationTitleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        static async IAsyncEnumerable<IChatUpdate> Throwing()
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
