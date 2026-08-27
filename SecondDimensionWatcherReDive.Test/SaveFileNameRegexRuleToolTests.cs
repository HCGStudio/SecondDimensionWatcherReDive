using Moq;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Inference;
using SecondDimensionWatcherReDive.Inference.AI.Tools;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class SaveFileNameRegexRuleToolTests
{
    [TestMethod]
    public async Task ExecuteCoreAsync_ValidRule_SavesForCurrentAnimationAndReturnsEveryMatch()
    {
        var animationId = Guid.NewGuid();
        const string pattern = @"^Anime\.S(?<season>\d{2})E(?<episode>\d{2})\.mkv$";
        var repository = new Mock<IFileNameRegexRuleRepository>();
        FileNameRegexRule? savedRule = null;
        repository
            .Setup(repo => repo.GetOrAddAsync(
                It.IsAny<FileNameRegexRule>(),
                CancellationToken.None))
            .ReturnsAsync((FileNameRegexRule rule, CancellationToken _) =>
            {
                savedRule = rule;
                return rule;
            });

        var context = new FileNameInferenceContext();
        var tool = new SaveFileNameRegexRuleTool(repository.Object, context);
        using var scope = context.Push(new FileNameInferenceRequest(
            animationId,
            "Anime batch",
            [
                new FileNameInferenceInput("disc-1/first.mkv", "Anime.S02E07.mkv"),
                new FileNameInferenceInput("disc-2/second.mkv", "Anime.S02E08.mkv"),
                new FileNameInferenceInput("extras/preview.mkv", "preview.mkv")
            ],
            true));

        var toolResult = await tool.ExecuteCoreAsync(
            new SaveFileNameRegexRuleParams(pattern, "Anime SxxExx releases"),
            CancellationToken.None);

        var success = toolResult as ToolSuccessResult<SaveFileNameRegexRuleResult>;
        Assert.IsNotNull(success);
        Assert.IsTrue(success.IsSuccess);
        Assert.IsTrue(success.Result.Created);
        Assert.IsNotNull(savedRule);
        Assert.AreEqual(success.Result.RuleId, savedRule.Id);
        Assert.AreEqual(animationId, savedRule.AnimationId);
        Assert.AreEqual(pattern, savedRule.Pattern);
        Assert.AreEqual("Anime SxxExx releases", savedRule.Description);
        Assert.AreEqual(2, success.Result.Matches.Count);

        Assert.AreEqual("disc-1/first.mkv", success.Result.Matches[0].FilePath);
        Assert.AreEqual("Anime.S02E07.mkv", success.Result.Matches[0].FileName);
        Assert.AreEqual(2, success.Result.Matches[0].Season);
        Assert.AreEqual(7, success.Result.Matches[0].Episode);
        Assert.AreEqual("disc-2/second.mkv", success.Result.Matches[1].FilePath);
        Assert.AreEqual("Anime.S02E08.mkv", success.Result.Matches[1].FileName);
        Assert.AreEqual(2, success.Result.Matches[1].Season);
        Assert.AreEqual(8, success.Result.Matches[1].Episode);
        CollectionAssert.AreEqual(
            new[] { "extras/preview.mkv" },
            success.Result.UnmatchedFiles.ToArray());

        repository.Verify(repo => repo.GetOrAddAsync(
            It.Is<FileNameRegexRule>(rule =>
                rule.AnimationId == animationId
                && rule.Pattern == pattern
                && rule.Description == "Anime SxxExx releases"),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    [DataRow(@"^Anime\.(\d+)\.mkv$", "episode")]
    [DataRow(@"(?<episode>[", "Invalid regex pattern")]
    [DataRow(@"^Other-(?<episode>\d+)\.mkv$", "did not extract an episode")]
    public async Task ExecuteCoreAsync_InvalidOrUnmatchedRule_FailsWithoutSaving(
        string pattern,
        string expectedError)
    {
        var animationId = Guid.NewGuid();
        var repository = new Mock<IFileNameRegexRuleRepository>();
        var context = new FileNameInferenceContext();
        var tool = new SaveFileNameRegexRuleTool(repository.Object, context);
        using var scope = context.Push(new FileNameInferenceRequest(
            animationId,
            "Anime batch",
            [new FileNameInferenceInput("Anime.07.mkv", "Anime.07.mkv")],
            true));

        var toolResult = await tool.ExecuteCoreAsync(
            new SaveFileNameRegexRuleParams(pattern, null),
            CancellationToken.None);

        var failure = toolResult as ToolFailureResult;
        Assert.IsNotNull(failure);
        Assert.IsFalse(failure.IsSuccess);
        StringAssert.Contains(failure.Error, expectedError);
        repository.Verify(repo => repo.GetOrAddAsync(
            It.IsAny<FileNameRegexRule>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task ExecuteCoreAsync_DuplicateRule_ReturnsExistingRuleWithoutAddingAgain()
    {
        var animationId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        const string pattern = @"^Anime-(?<episode>\d+)\.mkv$";
        var existingRule = new FileNameRegexRule(
            ruleId,
            animationId,
            pattern,
            "Existing format",
            DateTimeOffset.UtcNow.AddDays(-1));
        var repository = new Mock<IFileNameRegexRuleRepository>();
        repository
            .Setup(repo => repo.GetOrAddAsync(
                It.Is<FileNameRegexRule>(rule =>
                    rule.AnimationId == animationId && rule.Pattern == pattern),
                CancellationToken.None))
            .ReturnsAsync(existingRule);

        var context = new FileNameInferenceContext();
        var tool = new SaveFileNameRegexRuleTool(repository.Object, context);
        using var scope = context.Push(new FileNameInferenceRequest(
            animationId,
            "Anime batch",
            [new FileNameInferenceInput("Anime-12.mkv", "Anime-12.mkv")],
            true));

        var toolResult = await tool.ExecuteCoreAsync(
            new SaveFileNameRegexRuleParams(pattern, "Replacement description"),
            CancellationToken.None);

        var success = toolResult as ToolSuccessResult<SaveFileNameRegexRuleResult>;
        Assert.IsNotNull(success);
        Assert.IsFalse(success.Result.Created);
        Assert.AreEqual(ruleId, success.Result.RuleId);
        Assert.AreEqual(1, success.Result.Matches.Count);
        Assert.AreEqual(12, success.Result.Matches[0].Episode);
        repository.Verify(repo => repo.GetOrAddAsync(
            It.Is<FileNameRegexRule>(rule =>
                rule.AnimationId == animationId && rule.Pattern == pattern),
            CancellationToken.None), Times.Once);
    }

    [TestMethod]
    public async Task ExecuteCoreAsync_RuleConflictsWithExistingResult_FailsWithoutSaving()
    {
        var animationId = Guid.NewGuid();
        var repository = new Mock<IFileNameRegexRuleRepository>();
        var context = new FileNameInferenceContext();
        var tool = new SaveFileNameRegexRuleTool(repository.Object, context);
        using var scope = context.Push(new FileNameInferenceRequest(
            animationId,
            "Anime batch",
            [
                new FileNameInferenceInput("Anime-01.mkv", "Anime-01.mkv"),
                new FileNameInferenceInput("Anime-02.mkv", "Anime-02.mkv")
            ],
            true,
            TargetFilePaths: ["Anime-02.mkv"],
            ExistingResults: [new FileNameInferenceResult("Anime-01.mkv", 2, 1)],
            DefaultSeason: 1));

        var toolResult = await tool.ExecuteCoreAsync(
            new SaveFileNameRegexRuleParams(@"^Anime-(?<episode>\d+)\.mkv$", null),
            CancellationToken.None);

        var failure = toolResult as ToolFailureResult;
        Assert.IsNotNull(failure);
        StringAssert.Contains(failure.Error, "conflicts with the existing result");
        repository.Verify(repo => repo.GetOrAddAsync(
            It.IsAny<FileNameRegexRule>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
