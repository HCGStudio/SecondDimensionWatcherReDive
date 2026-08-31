using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace SecondDimensionWatcherReDive.Auth;

internal readonly record struct BasicAuthenticationAttemptResult(
    bool IsAcquired,
    TimeSpan? RetryAfter);

internal interface IBasicAuthenticationAttemptStore
{
    ValueTask<BasicAuthenticationAttemptResult> AttemptAcquireAsync(
        string partitionKey,
        CancellationToken cancellationToken);
}

internal sealed class MemoryBasicAuthenticationAttemptStore : IBasicAuthenticationAttemptStore, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public MemoryBasicAuthenticationAttemptStore(IOptions<BasicAuthenticationRateLimitOptions> options)
        : this(
            options.Value.BasicPermitLimit,
            TimeSpan.FromSeconds(options.Value.BasicWindowSeconds))
    {
    }

    internal MemoryBasicAuthenticationAttemptStore(int permitLimit, TimeSpan window)
    {
        Validate(permitLimit, window);
        _limiter = PartitionedRateLimiter.Create<string, string>(partitionKey =>
            RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    }

    public ValueTask<BasicAuthenticationAttemptResult> AttemptAcquireAsync(
        string partitionKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = _limiter.AttemptAcquire(partitionKey);
        var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? retry
            : (TimeSpan?)null;
        return ValueTask.FromResult(new BasicAuthenticationAttemptResult(lease.IsAcquired, retryAfter));
    }

    public void Dispose() => _limiter.Dispose();

    private static void Validate(int permitLimit, TimeSpan window)
    {
        if (permitLimit <= 0)
            throw new InvalidOperationException("RateLimit:BasicPermitLimit must be positive.");
        if (window <= TimeSpan.Zero)
            throw new InvalidOperationException("RateLimit:BasicWindowSeconds must be positive.");
    }
}

internal sealed class RedisBasicAuthenticationAttemptStore(
    RedisConnectionProvider connectionProvider,
    string instanceName,
    IOptions<BasicAuthenticationRateLimitOptions> options) : IBasicAuthenticationAttemptStore
{
    private const string Script = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[2])
        end
        local ttl = redis.call('PTTL', KEYS[1])
        if count <= tonumber(ARGV[1]) then
            return { 1, ttl }
        end
        return { 0, ttl }
        """;

    private readonly int _permitLimit = options.Value.BasicPermitLimit;
    private readonly long _windowMilliseconds = checked(options.Value.BasicWindowSeconds * 1000L);
    private readonly string _keyPrefix = $"{instanceName}basic-auth-failure:";

    public async ValueTask<BasicAuthenticationAttemptResult> AttemptAcquireAsync(
        string partitionKey,
        CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
        var database = connection.GetDatabase();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(partitionKey)));
        var result = (RedisResult[]?)await database.ScriptEvaluateAsync(
                Script,
                [(RedisKey)(_keyPrefix + digest)],
                [_permitLimit, _windowMilliseconds])
            .WaitAsync(cancellationToken);
        if (result is not { Length: 2 })
            throw new InvalidOperationException("Valkey returned an invalid Basic rate-limit response.");

        var acquired = (long)result[0] == 1;
        var ttlMilliseconds = Math.Max(1, (long)result[1]);
        return new BasicAuthenticationAttemptResult(
            acquired,
            acquired ? null : TimeSpan.FromMilliseconds(ttlMilliseconds));
    }
}

internal sealed partial class BasicAuthenticationAttemptLimiter : IDisposable
{
    internal static readonly object RateLimitedItemKey = new();
    private readonly IBasicAuthenticationAttemptStore _store;
    private readonly ILogger<BasicAuthenticationAttemptLimiter> _logger;
    private readonly TimeSpan _failureRetryAfter;
    private readonly bool _ownsStore;

    public BasicAuthenticationAttemptLimiter(
        IBasicAuthenticationAttemptStore store,
        IOptions<BasicAuthenticationRateLimitOptions> options,
        ILogger<BasicAuthenticationAttemptLimiter> logger)
    {
        _store = store;
        _logger = logger;
        _failureRetryAfter = TimeSpan.FromSeconds(options.Value.BasicWindowSeconds);
        _ownsStore = false;
    }

    internal BasicAuthenticationAttemptLimiter(int permitLimit, TimeSpan window)
    {
        _store = new MemoryBasicAuthenticationAttemptStore(permitLimit, window);
        _logger = NullLogger<BasicAuthenticationAttemptLimiter>.Instance;
        _failureRetryAfter = window;
        _ownsStore = true;
    }

    internal async ValueTask<BasicAuthenticationAttemptResult> AttemptAcquireAsync(
        IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        var normalized = remoteAddress?.IsIPv4MappedToIPv6 is true
            ? remoteAddress.MapToIPv4()
            : remoteAddress;
        try
        {
            return await _store.AttemptAcquireAsync(
                normalized?.ToString() ?? "unknown",
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A configured shared store is a security boundary. Never fall back to a
            // per-process bucket when it is unavailable, because that silently expands
            // the brute-force budget across replicas.
            LogStoreUnavailable(_logger, exception);
            return new BasicAuthenticationAttemptResult(false, _failureRetryAfter);
        }
    }

    public void Dispose()
    {
        if (_ownsStore && _store is IDisposable disposable)
            disposable.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Basic authentication attempt store is unavailable; rejecting the failed attempt")]
    private static partial void LogStoreUnavailable(ILogger logger, Exception exception);
}

internal sealed class BasicAuthenticationRateLimitOptions
{
    internal const string SectionName = "RateLimit";

    [Range(1, 10_000)]
    public int BasicPermitLimit { get; set; } = 20;

    [Range(1, 3_600)]
    public int BasicWindowSeconds { get; set; } = 60;

    [Range(1, 10_000)]
    public int LogoutPermitLimit { get; set; } = 60;

    [Range(1, 3_600)]
    public int LogoutWindowSeconds { get; set; } = 60;
}
