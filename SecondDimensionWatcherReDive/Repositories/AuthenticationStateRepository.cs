using Microsoft.EntityFrameworkCore;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class AuthenticationStateRepository(Models.ApplicationContext context)
    : IAuthenticationStateRepository
{
    public async Task<string?> GetPasswordHashAsync(CancellationToken cancellationToken)
    {
        return await context.AuthenticationStates
            .AsNoTracking()
            .Where(state => state.Id == Models.AuthenticationState.SingletonId)
            .Select(state => state.PasswordHash)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryClaimPasswordAsync(
        string passwordHash,
        Guid claimId,
        DateTimeOffset registeredAt,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var affected = await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO "AuthenticationStates" ("Id", "PasswordHash", "ClaimId", "RegisteredAt")
                 VALUES ({Models.AuthenticationState.SingletonId}, {passwordHash}, {claimId}, {registeredAt})
                 ON CONFLICT ("Id") DO NOTHING
                 """,
                cancellationToken);
            if (affected == 1)
                return true;

            // A transient connection failure can occur after PostgreSQL committed the INSERT.
            // Recognizing our own claim makes the retry idempotent without admitting a
            // concurrent claimant, whose opaque claim identifier is necessarily different.
            return await context.AuthenticationStates
                .AsNoTracking()
                .AnyAsync(state =>
                        state.Id == Models.AuthenticationState.SingletonId && state.ClaimId == claimId,
                    cancellationToken);
        });
    }
}
