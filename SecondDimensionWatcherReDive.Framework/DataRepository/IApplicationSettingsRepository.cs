namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IApplicationSettingsRepository
{
    Task<ApplicationSettings?> GetAsync(CancellationToken cancellationToken);

    Task<ApplicationSettings?> TrySaveAsync(
        string valuesJson,
        string? protectedSecrets,
        long expectedRevision,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);
}
