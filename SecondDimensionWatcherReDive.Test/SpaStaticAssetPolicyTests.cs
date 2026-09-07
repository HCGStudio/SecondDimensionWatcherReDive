using SecondDimensionWatcherReDive.Utils.Spa;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class SpaStaticAssetPolicyTests
{
    [TestMethod]
    public void FingerprintedAssetsAreImmutable()
    {
        Assert.AreEqual(
            SpaStaticAssetPolicy.ImmutableCacheControl,
            SpaStaticAssetPolicy.CacheControlFor("PlayerPage.e77a7175.js"));
        Assert.AreEqual(
            SpaStaticAssetPolicy.ImmutableCacheControl,
            SpaStaticAssetPolicy.CacheControlFor("ffmpeg-core.1ef751a0.wasm"));
    }

    [TestMethod]
    public void HtmlAlwaysRevalidates()
    {
        Assert.AreEqual(
            SpaStaticAssetPolicy.RevalidateCacheControl,
            SpaStaticAssetPolicy.CacheControlFor("index.html"));
    }

    [TestMethod]
    public void UnfingerprintedAssetsUseAShortCache()
    {
        Assert.AreEqual(
            SpaStaticAssetPolicy.ShortCacheControl,
            SpaStaticAssetPolicy.CacheControlFor("manifest.webmanifest"));
    }
}
