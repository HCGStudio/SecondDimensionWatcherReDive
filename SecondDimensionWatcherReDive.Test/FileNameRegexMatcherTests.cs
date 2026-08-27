using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class FileNameRegexMatcherTests
{
    [TestMethod]
    public void Match_WithSeasonAndEpisode_ReturnsParsedValues()
    {
        var created = FileNameRegexMatcher.TryCreateRegex(
            @"S(?<season>\d+)E(?<episode>\d+)",
            out var regex,
            out var error);

        Assert.IsTrue(created, error);

        var result = FileNameRegexMatcher.Match(
            regex!,
            new FileNameInferenceInput("Season 2/episode.mkv", "Anime.S02E07.1080p.mkv"));

        Assert.IsNotNull(result);
        Assert.AreEqual("Season 2/episode.mkv", result.FilePath);
        Assert.AreEqual(2, result.Season);
        Assert.AreEqual(7, result.Episode);
    }

    [TestMethod]
    public void Match_WithoutSeasonGroup_ReturnsNullSeason()
    {
        var created = FileNameRegexMatcher.TryCreateRegex(
            @"Episode[ ._-]*(?<episode>\d+)",
            out var regex,
            out var error);

        Assert.IsTrue(created, error);

        var result = FileNameRegexMatcher.Match(
            regex!,
            new FileNameInferenceInput("Episode 12.mkv", "Episode 12.mkv"));

        Assert.IsNotNull(result);
        Assert.IsNull(result.Season);
        Assert.AreEqual(12, result.Episode);
    }

    [TestMethod]
    public void TryCreateRegex_WithoutEpisodeGroup_IsRejected()
    {
        var created = FileNameRegexMatcher.TryCreateRegex(
            @"S(?<season>\d+)E\d+",
            out var regex,
            out var error);

        Assert.IsFalse(created);
        Assert.IsNull(regex);
        StringAssert.Contains(error, "episode");
    }

    [TestMethod]
    [DataRow("Anime.Episode.abc.mkv", @"Episode[ ._-]*(?<episode>[^.]+)")]
    [DataRow("Anime.Episode.-1.mkv", @"Episode[ ._]*(?<episode>-?\d+)")]
    public void Match_WithNonNumericOrNegativeEpisode_ReturnsNull(string fileName, string pattern)
    {
        var created = FileNameRegexMatcher.TryCreateRegex(pattern, out var regex, out var error);
        Assert.IsTrue(created, error);

        var result = FileNameRegexMatcher.Match(
            regex!,
            new FileNameInferenceInput(fileName, fileName));

        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryCreateRegex_OverMaximumLength_IsRejected()
    {
        var pattern = new string('a', FileNameRegexMatcher.MaxPatternLength + 1);

        var created = FileNameRegexMatcher.TryCreateRegex(pattern, out var regex, out var error);

        Assert.IsFalse(created);
        Assert.IsNull(regex);
        StringAssert.Contains(error, FileNameRegexMatcher.MaxPatternLength.ToString());
    }

    [TestMethod]
    public void TryCreateRegex_InvalidPattern_IsRejected()
    {
        var created = FileNameRegexMatcher.TryCreateRegex(
            @"(?<episode>[",
            out var regex,
            out var error);

        Assert.IsFalse(created);
        Assert.IsNull(regex);
        StringAssert.Contains(error, "Invalid regex pattern");
    }

    [TestMethod]
    public void TryCreateRegex_BacktrackingOnlyConstruct_IsRejected()
    {
        var created = FileNameRegexMatcher.TryCreateRegex(
            @"^(?<episode>\d+)-\k<episode>$",
            out var regex,
            out var error);

        Assert.IsFalse(created);
        Assert.IsNull(regex);
        StringAssert.Contains(error, "Invalid regex pattern");
    }
}
