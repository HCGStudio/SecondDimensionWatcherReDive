using SecondDimensionWatcherReDive.Auth;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class DevicePathScopeTests
{
    [TestMethod]
    public void ScopedRoot_MapsPublicRootAndChildren()
    {
        Assert.IsTrue(DevicePathScope.TryMapPublicToInternal(
            "/", "/Anime", out var publicRoot, out var internalRoot));
        Assert.AreEqual("/", publicRoot);
        Assert.AreEqual("/Anime", internalRoot);

        Assert.IsTrue(DevicePathScope.TryMapPublicToInternal(
            "/Season/episode.mkv", "/Anime", out var publicChild, out var internalChild));
        Assert.AreEqual("/Season/episode.mkv", publicChild);
        Assert.AreEqual("/Anime/Season/episode.mkv", internalChild);
    }

    [TestMethod]
    public void InternalMapping_UsesPathSegments_NotStringPrefixes()
    {
        Assert.IsTrue(DevicePathScope.TryMapInternalToPublic(
            "/Anime/episode.mkv", "/Anime", out var publicPath));
        Assert.AreEqual("/episode.mkv", publicPath);

        Assert.IsFalse(DevicePathScope.TryMapInternalToPublic(
            "/Anime2/private.mkv", "/Anime", out _));
    }

    [TestMethod]
    public void TraversalOrBackslash_IsRejected()
    {
        Assert.IsFalse(DevicePathScope.TryMapPublicToInternal(
            "/../Anime2/private.mkv", "/Anime", out _, out _));
        Assert.IsFalse(DevicePathScope.TryMapPublicToInternal(
            "/Season\\private.mkv", "/Anime", out _, out _));
    }
}
