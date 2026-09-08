using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Auth;

internal sealed partial class AuthenticationStateInitializer(
    IConfiguration configuration,
    IAuthenticationStateRepository repository,
    TimeProvider timeProvider,
    ILogger<AuthenticationStateInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var deploymentHash = configuration["Authentication:BootstrapPasswordHash"];
        if (string.IsNullOrWhiteSpace(deploymentHash))
            return;

        var databaseHash = await repository.GetPasswordHashAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(databaseHash))
        {
            await repository.TryClaimPasswordAsync(
                deploymentHash,
                Guid.NewGuid(),
                timeProvider.GetUtcNow(),
                cancellationToken);
            databaseHash = await repository.GetPasswordHashAsync(cancellationToken);
        }

        if (!string.Equals(databaseHash, deploymentHash, StringComparison.Ordinal))
            LogBootstrapPasswordIgnored(logger);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Authentication:BootstrapPasswordHash differs from the authoritative database password and was ignored")]
    private static partial void LogBootstrapPasswordIgnored(ILogger logger);
}
