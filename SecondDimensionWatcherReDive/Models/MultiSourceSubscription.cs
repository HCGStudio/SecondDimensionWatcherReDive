using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace SecondDimensionWatcherReDive.Models;

public sealed class MultiSourceSubscription
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string TmdbId { get; set; } = "";
    public int Season { get; set; }
    public int WaitMinutes { get; set; }
    public string Mode { get; set; } = "ManualConfirm";
    public string[] SubtitleGroups { get; set; } = [];
    public string[] Resolutions { get; set; } = [];
    public string[] Codecs { get; set; } = [];
    public string[] Languages { get; set; } = [];
    public long? MinSizeBytes { get; set; }
    public long? MaxSizeBytes { get; set; }
    public string[] ExcludedKeywords { get; set; } = [];
    public bool EnableVersionUpgrade { get; set; }
    public int MinimumUpgradeScore { get; set; }
    public int UpgradeRollbackHours { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<MultiSourceFeed> Sources { get; set; } = [];
}
public sealed class MultiSourceFeed
{
    public Guid FeedId { get; set; }
    public Guid SubscriptionId { get; set; }
    public int Priority { get; set; }
}
public sealed class MultiSourceEpisodeDecision
{
    public Guid SubscriptionId { get; set; }
    public int Episode { get; set; }
    public DateTimeOffset WaitStartedAt { get; set; }
    public DateTimeOffset WaitUntil { get; set; }
    public Guid? SelectedReleaseId { get; set; }
    public string Outcome { get; set; } = "waiting";
    public string Reason { get; set; } = "waiting_for_primary";
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class MultiSourceSubscriptionConfiguration : IEntityTypeConfiguration<MultiSourceSubscription>
{
    public void Configure(EntityTypeBuilder<MultiSourceSubscription> b)
    {
        b.ToTable("MultiSourceSubscriptions"); b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.TmdbId).HasMaxLength(32);
        b.HasIndex(x => new { x.TmdbId, x.Season }).IsUnique();
        b.HasMany(x => x.Sources).WithOne().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
public sealed class MultiSourceFeedConfiguration : IEntityTypeConfiguration<MultiSourceFeed>
{
    public void Configure(EntityTypeBuilder<MultiSourceFeed> b)
    {
        b.ToTable("MultiSourceFeeds"); b.HasKey(x => x.FeedId);
        b.HasOne<Feed>().WithMany().HasForeignKey(x => x.FeedId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.SubscriptionId, x.Priority });
    }
}
public sealed class MultiSourceEpisodeDecisionConfiguration : IEntityTypeConfiguration<MultiSourceEpisodeDecision>
{
    public void Configure(EntityTypeBuilder<MultiSourceEpisodeDecision> b)
    {
        b.ToTable("MultiSourceEpisodeDecisions"); b.HasKey(x => new { x.SubscriptionId, x.Episode });
        b.HasOne<MultiSourceSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
    }
}
