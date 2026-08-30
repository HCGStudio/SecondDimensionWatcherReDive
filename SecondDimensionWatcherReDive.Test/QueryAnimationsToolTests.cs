using System.Text.Json;
using Moq;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Chat.Tools;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class QueryAnimationsToolTests
{
    [TestMethod]
    public async Task GroupedQuery_ReturnsCompositeCursorAndDoesNotRestartCompletedSection()
    {
        Assert.IsNotNull(QueryAnimationsTool.Definition);
        var now = DateTimeOffset.UtcNow;
        var animationCursor = new AnimationCatalogCursor(now, "200");
        var uncategorizedCursor = new AnimationInfoCursor(now, Guid.NewGuid());
        var repository = new Mock<IAnimationInfoRepository>(MockBehavior.Strict);
        repository.Setup(candidate => candidate.GetAnimationCatalogPageAsync(
                null,
                1,
                CancellationToken.None))
            .ReturnsAsync(new AnimationCatalogPage(
                [CatalogItem("300", now)],
                animationCursor));
        repository.Setup(candidate => candidate.GetUncategorizedPageAsync(
                null,
                1,
                CancellationToken.None))
            .ReturnsAsync(new AnimationInfoSummaryPage(
                [Summary(uncategorizedCursor.Id, now)],
                null));
        repository.Setup(candidate => candidate.GetAnimationCatalogPageAsync(
                animationCursor,
                1,
                CancellationToken.None))
            .ReturnsAsync(new AnimationCatalogPage(
                [CatalogItem("200", now.AddMinutes(-1))],
                null));
        var tool = new QueryAnimationsTool(repository.Object);

        var firstResult = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new QueryAnimationsParams(QueryAnimationsAction.Grouped, Take: 1),
                ToolJsonOptions.Options),
            CancellationToken.None);

        var first = firstResult as ToolSuccessResult<AnimationGroupedToolResult>;
        Assert.IsNotNull(first);
        Assert.AreEqual(1, first.Result.ReturnedAnimationCount);
        Assert.AreEqual(1, first.Result.ReturnedUncategorizedCount);
        Assert.IsTrue(first.Result.IsTruncated);
        Assert.IsNotNull(first.Result.NextCursor);
        Assert.AreEqual(animationCursor, first.Result.NextCursor.Animations);
        Assert.IsFalse(first.Result.NextCursor.AnimationsComplete);
        Assert.IsTrue(first.Result.NextCursor.UncategorizedComplete);

        var secondResult = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new QueryAnimationsParams(
                    QueryAnimationsAction.Grouped,
                    Take: 1,
                    GroupedCursor: first.Result.NextCursor),
                ToolJsonOptions.Options),
            CancellationToken.None);

        var second = secondResult as ToolSuccessResult<AnimationGroupedToolResult>;
        Assert.IsNotNull(second);
        Assert.AreEqual(1, second.Result.ReturnedAnimationCount);
        Assert.AreEqual(0, second.Result.ReturnedUncategorizedCount);
        Assert.IsFalse(second.Result.IsTruncated);
        Assert.IsNull(second.Result.NextCursor);
        repository.Verify(candidate => candidate.GetUncategorizedPageAsync(
            It.IsAny<AnimationInfoCursor?>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task GroupedQuery_NonzeroSkipFailsInsteadOfSilentlyIgnoringIt()
    {
        var repository = new Mock<IAnimationInfoRepository>(MockBehavior.Strict);
        var tool = new QueryAnimationsTool(repository.Object);

        var result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new QueryAnimationsParams(QueryAnimationsAction.Grouped, Skip: 20),
                ToolJsonOptions.Options),
            CancellationToken.None);

        var failure = result as ToolFailureResult;
        Assert.IsNotNull(failure);
        StringAssert.Contains(failure.Error, "grouped_cursor");
    }

    private static AnimationCatalogItem CatalogItem(string tmdbId, DateTimeOffset publishedAt) =>
        new(tmdbId, "Anime " + tmdbId, "Anime " + tmdbId, null, 1, 1, 0, publishedAt);

    private static AnimationInfoSummary Summary(Guid id, DateTimeOffset publishedAt) =>
        new(
            id,
            "Uncategorized",
            "Description",
            publishedAt,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            false);
}
