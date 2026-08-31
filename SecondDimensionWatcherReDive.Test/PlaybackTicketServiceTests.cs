using Microsoft.AspNetCore.DataProtection;
using SecondDimensionWatcherReDive.Auth;

namespace SecondDimensionWatcherReDive.Test;

[TestClass]
public sealed class PlaybackTicketServiceTests
{
    private const string Pepper = "playback-ticket-test-pepper-with-at-least-32-bytes";

    [TestMethod]
    public void Tickets_AreSessionBound_ReplayableForRanges_AndExpireFailClosed()
    {
        var time = new RefreshTokenStoreTests.ManualTimeProvider();
        var service = new PlaybackTicketService(
            new EphemeralDataProtectionProvider(),
            new DeviceTokenHasher(Pepper),
            time);
        var video = service.Issue("user-1", "jwt-1", "/video.mkv", TimeSpan.FromMinutes(2));
        time.Advance(TimeSpan.FromSeconds(1));
        var subtitle = service.Issue("user-1", "jwt-1", "/video.zh.srt", TimeSpan.FromMinutes(2));
        var otherSession = service.Issue("user-1", "jwt-2", "/video.mkv", TimeSpan.FromMinutes(2));

        Assert.AreEqual("/video.mkv", service.Validate(
            video.ResourceId, subtitle.CookieCredential)?.Path);
        Assert.AreEqual("/video.zh.srt", service.Validate(
            subtitle.ResourceId, video.CookieCredential)?.Path);
        // Byte-range playback necessarily reuses the same pair while it is valid.
        Assert.AreEqual("/video.mkv", service.Validate(
            video.ResourceId, subtitle.CookieCredential)?.Path);
        Assert.IsNull(service.Validate(video.ResourceId, null));
        Assert.IsNull(service.Validate(video.ResourceId, otherSession.CookieCredential));

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.IsNull(service.Validate(video.ResourceId, video.CookieCredential));
        Assert.IsNull(service.Validate(subtitle.ResourceId, subtitle.CookieCredential));
    }
}
