using SecondDimensionWatcherReDive.Services.Transcoding;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public class TranscodingPlannerTests
{
    [TestMethod]
    public void CreatePlan_BrowserCompatibleMp4_UsesDirectPlay()
    {
        var plan = TranscodingPlanner.CreatePlan(
            CreateSource("episode.mp4"),
            CreateProbe(Video("h264"), Audio("aac")),
            TranscodingSelection.Create("auto", null, null, null, null),
            burnBitmapSubtitles: false);

        Assert.AreEqual(TranscodingStrategy.Direct, plan.Strategy);
        Assert.IsTrue(plan.CopyVideo);
        Assert.IsTrue(plan.CopyAudio);
    }

    [TestMethod]
    public void CreatePlan_CompatibleTracksInMkv_UsesLosslessRemux()
    {
        var plan = TranscodingPlanner.CreatePlan(
            CreateSource("episode.mkv"),
            CreateProbe(Video("h264"), Audio("aac")),
            TranscodingSelection.Create("auto", null, null, null, null),
            burnBitmapSubtitles: false);

        Assert.AreEqual(TranscodingStrategy.Remux, plan.Strategy);
        Assert.IsTrue(plan.CopyVideo);
        Assert.IsTrue(plan.CopyAudio);
    }

    [TestMethod]
    public void CreatePlan_Hi10pH264_TranscodesToBrowserCompatibleVideo()
    {
        var plan = TranscodingPlanner.CreatePlan(
            CreateSource("episode.mkv"),
            CreateProbe(Video("h264", "High 10", "yuv420p10le"), Audio("aac")),
            TranscodingSelection.Create("auto", null, null, null, null),
            burnBitmapSubtitles: false);

        Assert.AreEqual(TranscodingStrategy.Transcode, plan.Strategy);
        Assert.IsFalse(plan.CopyVideo);
        Assert.IsTrue(plan.CopyAudio);
    }

    [TestMethod]
    public void CreatePlan_H264WithoutCompatibilityMetadata_FailsClosedToTranscode()
    {
        var plan = TranscodingPlanner.CreatePlan(
            CreateSource("episode.mp4"),
            CreateProbe(Video("h264", null, null), Audio("aac")),
            TranscodingSelection.Create("auto", null, null, null, null),
            burnBitmapSubtitles: false);

        Assert.AreEqual(TranscodingStrategy.Transcode, plan.Strategy);
        Assert.IsFalse(plan.CopyVideo);
    }

    [TestMethod]
    public void CreatePlan_UnsupportedCodecs_TranscodesAndSelectsPreferredAudio()
    {
        var japanese = Audio("flac", 1, "jpn", "Japanese");
        var english = Audio("aac", 2, "eng", "English", isDefault: true);
        var plan = TranscodingPlanner.CreatePlan(
            CreateSource("episode.mkv"),
            CreateProbe(Video("hevc"), japanese, english),
            TranscodingSelection.Create("720p", "ja", null, null, null),
            burnBitmapSubtitles: false);

        Assert.AreEqual(TranscodingStrategy.Transcode, plan.Strategy);
        Assert.AreEqual(japanese.Index, plan.Audio?.Index);
        Assert.IsFalse(plan.CopyVideo);
        Assert.IsFalse(plan.CopyAudio);
    }

    [TestMethod]
    public void CreatePlan_TextSubtitlesBecomeWebVttAndBitmapTrackCanBeBurned()
    {
        var ass = Subtitle("ass", 2, "eng", "English signs");
        var pgs = Subtitle("hdmv_pgs_subtitle", 3, "zho", "Chinese PGS");
        var plan = TranscodingPlanner.CreatePlan(
            CreateSource("episode.mkv"),
            CreateProbe(Video("h264"), Audio("aac"), ass, pgs),
            TranscodingSelection.Create("auto", null, null, "zh", null),
            burnBitmapSubtitles: true);

        Assert.AreEqual(TranscodingStrategy.Transcode, plan.Strategy);
        CollectionAssert.AreEqual(new[] { ass.Index }, plan.TextSubtitles.Select(item => item.Index).ToArray());
        Assert.AreEqual(pgs.Index, plan.BitmapSubtitleToBurn?.Index);
        Assert.AreEqual(0, plan.UnsupportedSubtitleCount);
    }

    [TestMethod]
    public void CreatePlan_SubtitlesOffNeverBurnsDefaultBitmapTrack()
    {
        var plan = TranscodingPlanner.CreatePlan(
            CreateSource("episode.mkv"),
            CreateProbe(
                Video("h264"),
                Audio("aac"),
                Subtitle("hdmv_pgs_subtitle", 3, "zho", "Chinese PGS")),
            TranscodingSelection.Create("auto", null, null, "off", null),
            burnBitmapSubtitles: true);

        Assert.IsNull(plan.BitmapSubtitleToBurn);
        Assert.AreEqual(TranscodingStrategy.Remux, plan.Strategy);
        Assert.AreEqual(1, plan.UnsupportedSubtitleCount);
    }

    [TestMethod]
    public void BuildCacheKey_ChangesForSourceVersionTrackAndQuality()
    {
        var source = CreateSource("episode.mkv");
        var baseline = source.BuildCacheKey(
            TranscodingSelection.Create("auto", "ja", null, "zh", null));

        var changedSource = source with { LastModifiedUtc = source.LastModifiedUtc.AddSeconds(1) };
        var changedTrack = source.BuildCacheKey(
            TranscodingSelection.Create("auto", "en", null, "zh", null));
        var changedQuality = source.BuildCacheKey(
            TranscodingSelection.Create("720p", "ja", null, "zh", null));

        Assert.AreNotEqual(baseline, changedSource.BuildCacheKey(
            TranscodingSelection.Create("auto", "ja", null, "zh", null)));
        Assert.AreNotEqual(baseline, changedTrack);
        Assert.AreNotEqual(baseline, changedQuality);
    }

    [TestMethod]
    public void ToProgressFraction_UsesMediaDurationAndClamps()
    {
        Assert.AreEqual(0.5, FfmpegProcessRunner.ToProgressFraction(50, TimeSpan.FromSeconds(100)));
        Assert.AreEqual(1, FfmpegProcessRunner.ToProgressFraction(110, TimeSpan.FromSeconds(100)));
        Assert.IsNull(FfmpegProcessRunner.ToProgressFraction(1, null));
    }

    private static TranscodingSource CreateSource(string fileName)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"/Anime/Group/{fileName}",
            $"/media/{fileName}",
            "test",
            fileName,
            1024,
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private static MediaProbe CreateProbe(params MediaStreamProbe[] streams)
        => new("matroska", TimeSpan.FromMinutes(24), streams);

    private static MediaStreamProbe Video(
        string codec,
        string? profile = "High",
        string? pixelFormat = "yuv420p")
        => new(0, "video", codec, null, null, true, false, false, profile, pixelFormat);

    private static MediaStreamProbe Audio(
        string codec,
        int index = 1,
        string? language = null,
        string? title = null,
        bool isDefault = false)
        => new(index, "audio", codec, language, title, isDefault, false, false, null, null);

    private static MediaStreamProbe Subtitle(
        string codec,
        int index,
        string? language,
        string? title)
        => new(index, "subtitle", codec, language, title, false, false, false, null, null);
}
