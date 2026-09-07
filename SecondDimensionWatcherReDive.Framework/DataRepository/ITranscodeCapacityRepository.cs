namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record TranscodeCapacityReservation(
    Guid Id, string DirectoryPath, string? VolumeIdentity, bool CountsAgainstDownloads, long BudgetBytes,
    long WrittenBytes, DateTimeOffset LeaseUntil);

public interface ITranscodeCapacityRepository
{
    Task<IReadOnlyList<TranscodeCapacityReservation>> ListActiveAsync(CancellationToken cancellationToken);
    Task AddAsync(Guid id, string directoryPath, string? volumeIdentity, bool countsAgainstDownloads, long budgetBytes,
        int leaseSeconds, CancellationToken cancellationToken);
    Task<bool> RenewAsync(Guid id, long writtenBytes, int leaseSeconds, CancellationToken cancellationToken);
    Task<bool> ExtendLeaseAsync(Guid id, int leaseSeconds, CancellationToken cancellationToken);
    Task RemoveAsync(Guid id, CancellationToken cancellationToken);
    Task PruneExpiredAsync(CancellationToken cancellationToken);
}
