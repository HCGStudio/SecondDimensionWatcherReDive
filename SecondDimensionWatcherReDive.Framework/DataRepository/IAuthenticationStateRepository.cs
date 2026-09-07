namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IAuthenticationStateRepository
{
    Task<string?> GetPasswordHashAsync(CancellationToken cancellationToken);

    Task<bool> TryClaimPasswordAsync(
        string passwordHash,
        Guid claimId,
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken);
}
