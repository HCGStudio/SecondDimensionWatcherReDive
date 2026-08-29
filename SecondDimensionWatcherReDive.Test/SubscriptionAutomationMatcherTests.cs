using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.Feed;
using SecondDimensionWatcherReDive.Framework.FileDownload;
using SecondDimensionWatcherReDive.Utils.Feed;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class SubscriptionAutomationMatcherTests
{
    private readonly ISubscriptionAutomationMatcher _matcher = new SubscriptionAutomationMatcher(
        new SubscriptionReleaseMetadataExtractor());

    [TestMethod]
    public void Evaluate_MatchingRelease_ReturnsMetadataAndSixPassingExplanations()
    {
        var release = Release(
            "[LoliHouse] My Anime - 01 [WebRip 1080p HEVC-10bit AAC][CHS&CHT&JPN]",
            1_500_000_000);
        var policy = Policy(
            subtitleGroups: ["lolihouse"],
            resolutions: ["1080P"],
            codecs: ["H.265"],
            languages: ["CHT"],
            minSizeBytes: 1_000_000_000,
            maxSizeBytes: 2_000_000_000);

        var result = _matcher.Evaluate(policy, release);

        Assert.IsTrue(result.Matched);
        Assert.AreEqual("LoliHouse", result.Metadata.SubtitleGroup);
        Assert.AreEqual("1080p", result.Metadata.Resolution);
        Assert.AreEqual("HEVC", result.Metadata.Codec);
        CollectionAssert.AreEqual(
            new[] { "简体中文", "繁體中文", "日语" },
            result.Metadata.Languages.ToArray());
        Assert.AreEqual(1_500_000_000, result.Metadata.SizeBytes);
        Assert.HasCount(6, result.Explanations);
        Assert.IsTrue(result.Explanations.All(explanation => explanation.Passed));
    }

    [TestMethod]
    public void Evaluate_ExcludedKeywordAndOutOfRange_ExplainsEveryFailure()
    {
        var release = Release(
            "[Other] My Anime NCOP [720p x264][ENG]",
            300_000_000);
        var policy = Policy(
            subtitleGroups: ["LoliHouse"],
            resolutions: ["4K"],
            codecs: ["AV1"],
            languages: ["简体中文"],
            minSizeBytes: 500_000_000,
            excludedKeywords: ["NCOP", "合集"]);

        var result = _matcher.Evaluate(policy, release);

        Assert.IsFalse(result.Matched);
        Assert.IsFalse(Explanation(result, "subtitleGroup").Passed);
        Assert.IsFalse(Explanation(result, "resolution").Passed);
        Assert.IsFalse(Explanation(result, "codec").Passed);
        Assert.IsFalse(Explanation(result, "languages").Passed);
        Assert.IsFalse(Explanation(result, "size").Passed);
        var exclusion = Explanation(result, "excludedKeywords");
        Assert.IsFalse(exclusion.Passed);
        Assert.AreEqual("NCOP", exclusion.Actual);
        StringAssert.Contains(exclusion.Message, "NCOP");
    }

    [TestMethod]
    public void Evaluate_UnrestrictedFields_PassEvenWhenMetadataIsMissing()
    {
        var result = _matcher.Evaluate(Policy(), Release("My Anime - 01", null));

        Assert.IsTrue(result.Matched);
        Assert.IsTrue(result.Explanations.All(explanation => explanation.Passed));
    }

    [TestMethod]
    public void Extract_AdditionalInfoProvidesHumanReadableSizeAndTechnicalTags()
    {
        var extractor = new SubscriptionReleaseMetadataExtractor();
        var release = Release("[Group] My Anime", null) with
        {
            AdditionalDownloadInfo = "quality=UHD; codec=AV1; language=EN; size=1.5 GiB"
        };

        var metadata = extractor.Extract(release);

        Assert.AreEqual("2160p", metadata.Resolution);
        Assert.AreEqual("AV1", metadata.Codec);
        CollectionAssert.AreEqual(new[] { "英语" }, metadata.Languages.ToArray());
        Assert.AreEqual(1_610_612_736L, metadata.SizeBytes);
    }

    [TestMethod]
    public void Extract_SubtitleGroupSkipsLeadingTechnicalTags()
    {
        var extractor = new SubscriptionReleaseMetadataExtractor();

        var metadata = extractor.Extract(Release(
            "[1080p HEVC][CHS&JPN]【LoliHouse】 Anime - 01",
            1_000));

        Assert.AreEqual("LoliHouse", metadata.SubtitleGroup);
    }

    [TestMethod]
    public void Extract_CommonDimensionAndCompactChineseTags_AreRecognized()
    {
        var extractor = new SubscriptionReleaseMetadataExtractor();

        var metadata = extractor.Extract(Release(
            "[Group] Anime - 01 [1920x1080 HEVC][简繁内封]",
            1_000));

        Assert.AreEqual("1080p", metadata.Resolution);
        CollectionAssert.AreEqual(
            new[] { "简体中文", "繁體中文" },
            metadata.Languages.ToArray());
    }

    [TestMethod]
    public void Evaluate_ValuesWithinOneFieldUseOr_AndDifferentFieldsUseAnd()
    {
        var release = Release("[Group] Anime [1080p HEVC][CHS]", 1_000);
        var matching = Policy(
            subtitleGroups: ["Other", "Group"],
            resolutions: ["720p", "1080p"],
            codecs: ["AV1", "x265"],
            languages: ["日语", "简体中文"]);
        var differentFieldFails = matching with { Codecs = ["AV1", "AVC"] };

        Assert.IsTrue(_matcher.Evaluate(matching, release).Matched);
        Assert.IsFalse(_matcher.Evaluate(differentFieldFails, release).Matched);
    }

    [TestMethod]
    public void Evaluate_4KCodecAndChineseJapaneseAliasesAreNormalized()
    {
        var release = Release("[Group] Anime [UHD H.265][CHS&JPN]", 1_000);
        var policy = Policy(
            resolutions: ["4K"],
            codecs: ["HEVC"],
            languages: ["JA"]);

        var result = _matcher.Evaluate(policy, release);

        Assert.IsTrue(result.Matched);
        Assert.AreEqual("2160p", result.Metadata.Resolution);
        Assert.AreEqual("HEVC", result.Metadata.Codec);
        CollectionAssert.AreEqual(new[] { "简体中文", "日语" }, result.Metadata.Languages.ToArray());
    }

    [TestMethod]
    public void Evaluate_SizeBoundsAreInclusive()
    {
        var policy = Policy(minSizeBytes: 1_000, maxSizeBytes: 2_000);

        Assert.IsTrue(_matcher.Evaluate(policy, Release("Anime", 1_000)).Matched);
        Assert.IsTrue(_matcher.Evaluate(policy, Release("Anime", 2_000)).Matched);
        Assert.IsFalse(_matcher.Evaluate(policy, Release("Anime", 999)).Matched);
        Assert.IsFalse(_matcher.Evaluate(policy, Release("Anime", 2_001)).Matched);
    }

    [TestMethod]
    public void Evaluate_ExcludedKeywordVetoesOtherwiseMatchingRelease()
    {
        var release = Release("[Group] Anime [1080p HEVC][CHS] v2", 1_000);
        var policy = Policy(
            subtitleGroups: ["Group"],
            resolutions: ["1080p"],
            codecs: ["HEVC"],
            languages: ["CHS"],
            minSizeBytes: 1_000,
            maxSizeBytes: 1_000,
            excludedKeywords: ["v2"]);

        var result = _matcher.Evaluate(policy, release);

        Assert.IsFalse(result.Matched);
        Assert.IsTrue(result.Explanations.Where(item => item.Field != "excludedKeywords")
            .All(item => item.Passed));
        Assert.IsFalse(Explanation(result, "excludedKeywords").Passed);
    }

    [TestMethod]
    public void Evaluate_ExcludedKeywordOnlyAppliesToReleaseTitle()
    {
        var release = Release("[Group] Anime [1080p]", 1_000) with
        {
            Description = "Synopsis mentions an NCOP as part of the story."
        };

        var result = _matcher.Evaluate(
            Policy(excludedKeywords: ["NCOP"]),
            release);

        Assert.IsTrue(result.Matched);
        Assert.IsTrue(Explanation(result, "excludedKeywords").Passed);
    }

    [TestMethod]
    public void Score_CombinesQualityPreferencesAndReturnsExplainableReasons()
    {
        var scorer = new ReleaseScoringService();
        var metadata = new SubscriptionReleaseMetadata(
            "LoliHouse",
            "2160p",
            "AV1",
            ["ja", "zh-CN"],
            3L * 1024 * 1024 * 1024);
        var policy = Policy(
            subtitleGroups: ["Other", "LoliHouse"],
            languages: ["ja"]);

        var result = scorer.Score(metadata, policy);

        Assert.AreEqual(575, result.Value);
        CollectionAssert.Contains(result.Reasons.ToArray(), "resolution:2160p:+400");
        CollectionAssert.Contains(result.Reasons.ToArray(), "codec:AV1:+80");
        CollectionAssert.Contains(result.Reasons.ToArray(), "subtitleGroup:LoliHouse:+45");
        CollectionAssert.Contains(result.Reasons.ToArray(), "language:zh-CN:+5");
        CollectionAssert.Contains(result.Reasons.ToArray(), "size:3.00GiB:+25");
    }

    private static SubscriptionAutomationExplanation Explanation(
        SubscriptionAutomationEvaluation result,
        string field)
    {
        return result.Explanations.Single(explanation => explanation.Field == field);
    }

    private static AnimationAddRequest Release(string title, long? sizeBytes)
    {
        return new AnimationAddRequest(
            DateTimeOffset.Parse("2026-08-27T12:00:00+08:00"),
            title,
            string.Empty,
            "https://example.com/release.torrent",
            FileDownloadTypes.HttpDownload,
            string.Empty,
            Guid.NewGuid(),
            sizeBytes);
    }

    internal static SubscriptionAutomationPolicy Policy(
        IReadOnlyList<string>? subtitleGroups = null,
        IReadOnlyList<string>? resolutions = null,
        IReadOnlyList<string>? codecs = null,
        IReadOnlyList<string>? languages = null,
        long? minSizeBytes = null,
        long? maxSizeBytes = null,
        IReadOnlyList<string>? excludedKeywords = null,
        SubscriptionAutomationMode mode = SubscriptionAutomationMode.ManualConfirm,
        Guid? feedId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new SubscriptionAutomationPolicy(
            feedId ?? Guid.NewGuid(),
            subtitleGroups ?? [],
            resolutions ?? [],
            codecs ?? [],
            languages ?? [],
            minSizeBytes,
            maxSizeBytes,
            excludedKeywords ?? [],
            mode,
            now,
            now);
    }
}
