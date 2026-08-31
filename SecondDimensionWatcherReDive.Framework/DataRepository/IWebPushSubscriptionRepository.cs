namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record WebPushSubscription(
    Guid Id,
    string Endpoint,
    string P256Dh,
    string Auth,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastSuccessAt,
    DateTimeOffset? LastFailureAt,
    string? LastError);

public interface IWebPushSubscriptionRepository
{
    Task<WebPushSubscription> UpsertAsync(
        WebPushSubscription subscription,
        CancellationToken cancellationToken);

    Task<WebPushSubscription?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<WebPushSubscription>> GetAllAsync(
        CancellationToken cancellationToken);

    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> RemoveByEndpointAsync(
        string endpoint,
        CancellationToken cancellationToken);

    Task RecordSuccessAsync(
        Guid id,
        DateTimeOffset succeededAt,
        CancellationToken cancellationToken);

    Task RecordFailureAsync(
        Guid id,
        DateTimeOffset failedAt,
        string error,
        CancellationToken cancellationToken);
}
