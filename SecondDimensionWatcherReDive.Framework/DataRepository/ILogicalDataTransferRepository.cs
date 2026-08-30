namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface ILogicalDataTransferRepository
{
    Task<LogicalDataBundle> ExportAsync(
        LogicalDataCategory categories,
        Guid userId,
        string applicationVersion,
        CancellationToken cancellationToken);

    Task<LogicalImportResult> ImportAsync(
        LogicalDataBundle bundle,
        LogicalImportConflictStrategy conflictStrategy,
        Guid userId,
        CancellationToken cancellationToken);
}
