using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Models;

public class ReleaseUpgradeOperation
{
    public Guid Id { get; set; }
    public Guid CurrentReleaseId { get; set; }
    public AnimationInfo CurrentRelease { get; set; } = null!;
    public Guid CandidateReleaseId { get; set; }
    public AnimationInfo CandidateRelease { get; set; } = null!;
    public ReleaseUpgradeStatus Status { get; set; }
    public int CurrentScore { get; set; }
    public int CandidateScore { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset? RollbackUntil { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureSummary { get; set; }
    public DateTimeOffset? DownloadPreparedAt { get; set; }
    public DateTimeOffset? DownloadSubmittedAt { get; set; }
    public Guid? DownloadSubmissionLeaseId { get; set; }
    public DateTimeOffset? DownloadSubmissionLeaseUntil { get; set; }
    public bool DownloadCancellationRemoveFile { get; set; }
    public ICollection<ReleaseUpgradeMappingSnapshot> MappingSnapshots { get; set; } = [];
}

public class ReleaseUpgradeMappingSnapshot
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public ReleaseUpgradeOperation Operation { get; set; } = null!;
    public ReleaseUpgradeMappingKind Kind { get; set; }
    public Guid OriginalMappingId { get; set; }
    public Guid AnimationInfoId { get; set; }
    public string VirtualPath { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public string FileStore { get; set; } = string.Empty;
}
