using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using SubscriptionEntity = SecondDimensionWatcherReDive.Models.WebPushSubscription;

namespace SecondDimensionWatcherReDive.Repositories;

public sealed class WebPushSubscriptionRepository :
    Framework.DataRepository.IWebPushSubscriptionRepository
{
    public const int MaximumSubscriptions = 50;
    private const string ProtectorPurpose =
        "SecondDimensionWatcherReDive.WebPushSubscriptions.v1";

    private readonly Models.ApplicationContext _context;
    private readonly DbContextOptions<Models.ApplicationContext> _contextOptions;
    private readonly IDataProtector _protector;

    public WebPushSubscriptionRepository(
        Models.ApplicationContext context,
        DbContextOptions<Models.ApplicationContext> contextOptions,
        IDataProtectionProvider dataProtectionProvider)
    {
        _context = context;
        _contextOptions = contextOptions;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    }

    public async Task<Framework.DataRepository.WebPushSubscription> UpsertAsync(
        Framework.DataRepository.WebPushSubscription subscription,
        CancellationToken cancellationToken)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var writeContext = new Models.ApplicationContext(_contextOptions);
            await using var transaction = await writeContext.Database
                .BeginTransactionAsync(cancellationToken);
            // Serialize registration/count changes across app replicas so the
            // global subscription cap cannot be bypassed with parallel requests.
            await writeContext.Database.ExecuteSqlRawAsync(
                "SELECT pg_advisory_xact_lock(1396983639)",
                cancellationToken);

            var endpointHash = HashEndpoint(subscription.Endpoint);
            var entity = await writeContext.WebPushSubscriptions
                .SingleOrDefaultAsync(
                    item => item.EndpointHash == endpointHash,
                    cancellationToken);
            if (entity is null)
            {
                if (await writeContext.WebPushSubscriptions.CountAsync(cancellationToken)
                    >= MaximumSubscriptions)
                    throw new Framework.DataRepository.WebPushSubscriptionLimitExceededException();

                entity = new SubscriptionEntity
                {
                    Id = subscription.Id,
                    EndpointHash = endpointHash,
                    CreatedAt = subscription.CreatedAt
                };
                Apply(entity, subscription);
                await writeContext.WebPushSubscriptions.AddAsync(entity, cancellationToken);
            }
            else
            {
                Apply(entity, subscription);
            }

            await writeContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToRecord(entity);
        });
    }

    public async Task<Framework.DataRepository.WebPushSubscription?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await _context.WebPushSubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return entity is null ? null : ToRecord(entity);
    }

    public async Task<IReadOnlyList<Framework.DataRepository.WebPushSubscription>> GetAllAsync(
        CancellationToken cancellationToken) =>
        (await _context.WebPushSubscriptions
                .AsNoTracking()
                .OrderByDescending(item => item.UpdatedAt)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken))
            .Select(ToRecord)
            .ToList();

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken) =>
        await _context.WebPushSubscriptions
            .Where(item => item.Id == id)
            .ExecuteDeleteAsync(cancellationToken) == 1;

    public async Task<bool> RemoveByEndpointAsync(
        string endpoint,
        CancellationToken cancellationToken) =>
        await _context.WebPushSubscriptions
            .Where(item => item.EndpointHash == HashEndpoint(endpoint))
            .ExecuteDeleteAsync(cancellationToken) == 1;

    public async Task RecordSuccessAsync(
        Guid id,
        DateTimeOffset succeededAt,
        CancellationToken cancellationToken)
    {
        await _context.WebPushSubscriptions
            .Where(item => item.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LastSuccessAt, succeededAt)
                .SetProperty(item => item.LastError, (string?)null), cancellationToken);
    }

    public async Task RecordFailureAsync(
        Guid id,
        DateTimeOffset failedAt,
        string error,
        CancellationToken cancellationToken)
    {
        var safeError = error.Length <= 256 ? error : error[..256];
        await _context.WebPushSubscriptions
            .Where(item => item.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LastFailureAt, failedAt)
                .SetProperty(item => item.LastError, safeError), cancellationToken);
    }

    private void Apply(
        SubscriptionEntity entity,
        Framework.DataRepository.WebPushSubscription subscription)
    {
        entity.ProtectedEndpoint = _protector.Protect(subscription.Endpoint);
        entity.ProtectedP256Dh = _protector.Protect(subscription.P256Dh);
        entity.ProtectedAuth = _protector.Protect(subscription.Auth);
        entity.UpdatedAt = subscription.UpdatedAt;
        entity.LastError = null;
    }

    private Framework.DataRepository.WebPushSubscription ToRecord(
        SubscriptionEntity entity) => new(
        entity.Id,
        _protector.Unprotect(entity.ProtectedEndpoint),
        _protector.Unprotect(entity.ProtectedP256Dh),
        _protector.Unprotect(entity.ProtectedAuth),
        entity.CreatedAt,
        entity.UpdatedAt,
        entity.LastSuccessAt,
        entity.LastFailureAt,
        entity.LastError);

    private static string HashEndpoint(string endpoint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)))
            .ToLowerInvariant();
}
