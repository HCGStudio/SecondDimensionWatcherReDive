using System.Text.Json;
using StackExchange.Redis;

namespace SecondDimensionWatcherReDive.Auth;

internal interface IRefreshTokenStorage
{
    Task<bool> TryCreateAsync(
        string fingerprint,
        RefreshTokenState state,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<RefreshTokenReplacement?> RotateAsync(
        string fingerprint,
        string expectedJwtId,
        string replacementFingerprint,
        string replacementJwtId,
        string replacementToken,
        DateTimeOffset now,
        TimeSpan reuseGrace,
        CancellationToken cancellationToken);

    Task RevokeAsync(
        string fingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

internal sealed class MemoryRefreshTokenStorage : IRefreshTokenStorage
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, RefreshTokenState> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RefreshTokenState> _used = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExpiringReplacement> _duplicates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _revokedFamilies = new(StringComparer.Ordinal);
    private int _operationsUntilCleanup = 128;

    public Task<bool> TryCreateAsync(
        string fingerprint,
        RefreshTokenState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var nowMilliseconds = now.ToUnixTimeMilliseconds();
            MaybeCleanupExpired(nowMilliseconds);
            RemoveExpiredRevocation(state.FamilyId, nowMilliseconds);
            if (state.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds ||
                _revokedFamilies.ContainsKey(state.FamilyId) ||
                _active.ContainsKey(fingerprint))
                return Task.FromResult(false);

            _active[fingerprint] = state;
            return Task.FromResult(true);
        }
    }

    public Task<RefreshTokenReplacement?> RotateAsync(
        string fingerprint,
        string expectedJwtId,
        string replacementFingerprint,
        string replacementJwtId,
        string replacementToken,
        DateTimeOffset now,
        TimeSpan reuseGrace,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var nowMilliseconds = now.ToUnixTimeMilliseconds();
            MaybeCleanupExpired(nowMilliseconds);
            CleanupToken(fingerprint, nowMilliseconds);

            if (_active.TryGetValue(fingerprint, out var state))
            {
                RemoveExpiredRevocation(state.FamilyId, nowMilliseconds);
                if (!FixedTimeEquals(state.JwtId, expectedJwtId) ||
                    state.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds ||
                    _revokedFamilies.ContainsKey(state.FamilyId) ||
                    _active.ContainsKey(replacementFingerprint))
                {
                    if (_revokedFamilies.ContainsKey(state.FamilyId))
                        _active.Remove(fingerprint);
                    return Task.FromResult<RefreshTokenReplacement?>(null);
                }

                var replacement = new RefreshTokenReplacement(
                    expectedJwtId,
                    replacementJwtId,
                    replacementToken,
                    state.FamilyId,
                    state.ExpiresAtUnixTimeMilliseconds);
                _active.Remove(fingerprint);
                _used[fingerprint] = state;
                _active[replacementFingerprint] = new RefreshTokenState(
                    replacementJwtId,
                    state.FamilyId,
                    state.ExpiresAtUnixTimeMilliseconds);

                var duplicateExpiresAt = Math.Min(
                    state.ExpiresAtUnixTimeMilliseconds,
                    now.Add(reuseGrace).ToUnixTimeMilliseconds());
                if (duplicateExpiresAt > nowMilliseconds)
                    _duplicates[fingerprint] = new ExpiringReplacement(replacement, duplicateExpiresAt);

                return Task.FromResult<RefreshTokenReplacement?>(replacement);
            }

            if (_duplicates.TryGetValue(fingerprint, out var duplicate) &&
                duplicate.ExpiresAtUnixTimeMilliseconds > nowMilliseconds &&
                FixedTimeEquals(duplicate.Replacement.PreviousJwtId, expectedJwtId) &&
                !_revokedFamilies.ContainsKey(duplicate.Replacement.FamilyId))
                return Task.FromResult<RefreshTokenReplacement?>(duplicate.Replacement);

            if (!_used.TryGetValue(fingerprint, out var used) ||
                !FixedTimeEquals(used.JwtId, expectedJwtId) ||
                used.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds)
                return Task.FromResult<RefreshTokenReplacement?>(null);

            _revokedFamilies[used.FamilyId] = used.ExpiresAtUnixTimeMilliseconds;
            return Task.FromResult<RefreshTokenReplacement?>(null);
        }
    }

    public Task RevokeAsync(
        string fingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var nowMilliseconds = now.ToUnixTimeMilliseconds();
            MaybeCleanupExpired(nowMilliseconds);
            CleanupToken(fingerprint, nowMilliseconds);
            var state = _active.GetValueOrDefault(fingerprint) ?? _used.GetValueOrDefault(fingerprint);
            if (state is null)
                return Task.CompletedTask;

            _active.Remove(fingerprint);
            if (state.ExpiresAtUnixTimeMilliseconds > nowMilliseconds)
                _revokedFamilies[state.FamilyId] = state.ExpiresAtUnixTimeMilliseconds;
            return Task.CompletedTask;
        }
    }

    private void CleanupToken(string fingerprint, long nowMilliseconds)
    {
        if (_active.TryGetValue(fingerprint, out var active) &&
            active.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds)
            _active.Remove(fingerprint);
        if (_used.TryGetValue(fingerprint, out var used) &&
            used.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds)
            _used.Remove(fingerprint);
        if (_duplicates.TryGetValue(fingerprint, out var duplicate) &&
            duplicate.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds)
            _duplicates.Remove(fingerprint);
    }

    private void RemoveExpiredRevocation(string familyId, long nowMilliseconds)
    {
        if (_revokedFamilies.TryGetValue(familyId, out var expiresAt) && expiresAt <= nowMilliseconds)
            _revokedFamilies.Remove(familyId);
    }

    private void MaybeCleanupExpired(long nowMilliseconds)
    {
        if (--_operationsUntilCleanup > 0)
            return;

        _operationsUntilCleanup = 128;
        foreach (var fingerprint in _active
                     .Where(pair => pair.Value.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds)
                     .Select(pair => pair.Key)
                     .ToArray())
            _active.Remove(fingerprint);
        foreach (var fingerprint in _used
                     .Where(pair => pair.Value.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds)
                     .Select(pair => pair.Key)
                     .ToArray())
            _used.Remove(fingerprint);
        foreach (var fingerprint in _duplicates
                     .Where(pair => pair.Value.ExpiresAtUnixTimeMilliseconds <= nowMilliseconds)
                     .Select(pair => pair.Key)
                     .ToArray())
            _duplicates.Remove(fingerprint);
        foreach (var familyId in _revokedFamilies
                     .Where(pair => pair.Value <= nowMilliseconds)
                     .Select(pair => pair.Key)
                     .ToArray())
            _revokedFamilies.Remove(familyId);
    }

    private static bool FixedTimeEquals(string left, string right) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(left),
            System.Text.Encoding.UTF8.GetBytes(right));

    private sealed record ExpiringReplacement(
        RefreshTokenReplacement Replacement,
        long ExpiresAtUnixTimeMilliseconds);
}

internal sealed class RedisConnectionProvider(string connectionString) : IAsyncDisposable
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _connection = new(
        () =>
        {
            var configuration = ConfigurationOptions.Parse(connectionString);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.ConnectAsync(configuration);
        });

    public async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken) =>
        await _connection.Value.WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (!_connection.IsValueCreated)
            return;

        var connection = await _connection.Value;
        await connection.DisposeAsync();
    }
}

internal sealed class RedisRefreshTokenStorage(
    RedisConnectionProvider connectionProvider,
    string instanceName) : IRefreshTokenStorage
{
    private const string HashTag = "{sdw-auth-refresh}:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string TryCreateScript = """
        local expiredFamilies = redis.call('ZRANGEBYSCORE', KEYS[3], '-inf', ARGV[2], 'LIMIT', 0, 64)
        for _, familyId in ipairs(expiredFamilies) do
            redis.call('HDEL', KEYS[2], familyId)
            redis.call('ZREM', KEYS[3], familyId)
        end
        local revokedUntil = redis.call('HGET', KEYS[2], ARGV[4])
        if revokedUntil then
            if tonumber(revokedUntil) > tonumber(ARGV[2]) then
                return 0
            end
            redis.call('HDEL', KEYS[2], ARGV[4])
            redis.call('ZREM', KEYS[3], ARGV[4])
        end
        if tonumber(ARGV[3]) <= tonumber(ARGV[2]) then
            return 0
        end
        local ttl = tonumber(ARGV[3]) - tonumber(ARGV[2])
        local created = redis.call('SET', KEYS[1], ARGV[1], 'PX', ttl, 'NX')
        if created then return 1 end
        return 0
        """;

    private const string RotateScript = """
        local now = tonumber(ARGV[2])
        local expiredFamilies = redis.call('ZRANGEBYSCORE', KEYS[6], '-inf', now, 'LIMIT', 0, 64)
        for _, familyId in ipairs(expiredFamilies) do
            redis.call('HDEL', KEYS[5], familyId)
            redis.call('ZREM', KEYS[6], familyId)
        end
        local active = redis.call('GET', KEYS[1])
        if active then
            local ok, state = pcall(cjson.decode, active)
            if not ok or state.jwtId ~= ARGV[1] or tonumber(state.expiresAtUnixTimeMilliseconds) <= now then
                return { 'invalid' }
            end

            local revokedUntil = redis.call('HGET', KEYS[5], state.familyId)
            if revokedUntil and tonumber(revokedUntil) > now then
                redis.call('DEL', KEYS[1])
                return { 'invalid' }
            end
            if revokedUntil then
                redis.call('HDEL', KEYS[5], state.familyId)
                redis.call('ZREM', KEYS[6], state.familyId)
            end

            local expiresAt = tonumber(state.expiresAtUnixTimeMilliseconds)
            local ttl = expiresAt - now
            if redis.call('EXISTS', KEYS[4]) == 1 then
                return { 'invalid' }
            end
            local replacementState = cjson.encode({
                jwtId = ARGV[3],
                familyId = state.familyId,
                expiresAtUnixTimeMilliseconds = expiresAt
            })
            local duplicate = cjson.encode({
                previousJwtId = ARGV[1],
                replacementJwtId = ARGV[3],
                replacementToken = ARGV[4],
                familyId = state.familyId,
                expiresAtUnixTimeMilliseconds = expiresAt
            })

            redis.call('DEL', KEYS[1])
            redis.call('SET', KEYS[2], active, 'PX', ttl)
            redis.call('SET', KEYS[4], replacementState, 'PX', ttl)
            local duplicateTtl = math.min(tonumber(ARGV[5]), ttl)
            if duplicateTtl > 0 then
                redis.call('SET', KEYS[3], duplicate, 'PX', duplicateTtl)
            end
            return { 'rotated', duplicate }
        end

        local duplicate = redis.call('GET', KEYS[3])
        if duplicate then
            local ok, replacement = pcall(cjson.decode, duplicate)
            if ok and replacement.previousJwtId == ARGV[1] then
                local revokedUntil = redis.call('HGET', KEYS[5], replacement.familyId)
                if revokedUntil and tonumber(revokedUntil) > now then
                    return { 'invalid' }
                end
                return { 'duplicate', duplicate }
            end
            return { 'invalid' }
        end

        local used = redis.call('GET', KEYS[2])
        if not used then return { 'invalid' } end
        local ok, state = pcall(cjson.decode, used)
        if not ok or state.jwtId ~= ARGV[1] or tonumber(state.expiresAtUnixTimeMilliseconds) <= now then
            return { 'invalid' }
        end

        redis.call('HSET', KEYS[5], state.familyId, state.expiresAtUnixTimeMilliseconds)
        redis.call('ZADD', KEYS[6], state.expiresAtUnixTimeMilliseconds, state.familyId)
        return { 'replay' }
        """;

    private const string RevokeScript = """
        local expiredFamilies = redis.call('ZRANGEBYSCORE', KEYS[4], '-inf', ARGV[1], 'LIMIT', 0, 64)
        for _, familyId in ipairs(expiredFamilies) do
            redis.call('HDEL', KEYS[3], familyId)
            redis.call('ZREM', KEYS[4], familyId)
        end
        local stateJson = redis.call('GET', KEYS[1])
        if not stateJson then stateJson = redis.call('GET', KEYS[2]) end
        if not stateJson then return 0 end
        local ok, state = pcall(cjson.decode, stateJson)
        if not ok or tonumber(state.expiresAtUnixTimeMilliseconds) <= tonumber(ARGV[1]) then
            return 0
        end
        redis.call('DEL', KEYS[1])
        redis.call('HSET', KEYS[3], state.familyId, state.expiresAtUnixTimeMilliseconds)
        redis.call('ZADD', KEYS[4], state.expiresAtUnixTimeMilliseconds, state.familyId)
        return 1
        """;

    private readonly string _keyPrefix = HashTag + instanceName;

    public async Task<bool> TryCreateAsync(
        string fingerprint,
        RefreshTokenState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(cancellationToken);
        var result = await database.ScriptEvaluateAsync(
                TryCreateScript,
                [ActiveKey(fingerprint), RevokedHashKey(), RevokedExpirationKey()],
                [
                    JsonSerializer.Serialize(state, JsonOptions),
                    now.ToUnixTimeMilliseconds(),
                    state.ExpiresAtUnixTimeMilliseconds,
                    state.FamilyId
                ])
            .WaitAsync(cancellationToken);
        return (long)result == 1;
    }

    public async Task<RefreshTokenReplacement?> RotateAsync(
        string fingerprint,
        string expectedJwtId,
        string replacementFingerprint,
        string replacementJwtId,
        string replacementToken,
        DateTimeOffset now,
        TimeSpan reuseGrace,
        CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(cancellationToken);
        var result = (RedisResult[]?)await database.ScriptEvaluateAsync(
                RotateScript,
                [
                    ActiveKey(fingerprint),
                    UsedKey(fingerprint),
                    DuplicateKey(fingerprint),
                    ActiveKey(replacementFingerprint),
                    RevokedHashKey(),
                    RevokedExpirationKey()
                ],
                [
                    expectedJwtId,
                    now.ToUnixTimeMilliseconds(),
                    replacementJwtId,
                    replacementToken,
                    Math.Max(0, (long)reuseGrace.TotalMilliseconds)
                ])
            .WaitAsync(cancellationToken);
        if (result is not { Length: >= 2 } ||
            (result[0].ToString() != "rotated" && result[0].ToString() != "duplicate"))
            return null;

        return JsonSerializer.Deserialize<RefreshTokenReplacement>(result[1].ToString(), JsonOptions);
    }

    public async Task RevokeAsync(
        string fingerprint,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var database = await GetDatabaseAsync(cancellationToken);
        await database.ScriptEvaluateAsync(
                RevokeScript,
                [
                    ActiveKey(fingerprint),
                    UsedKey(fingerprint),
                    RevokedHashKey(),
                    RevokedExpirationKey()
                ],
                [now.ToUnixTimeMilliseconds()])
            .WaitAsync(cancellationToken);
    }

    private async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        var connection = await connectionProvider.GetConnectionAsync(cancellationToken);
        return connection.GetDatabase();
    }

    private RedisKey ActiveKey(string fingerprint) => $"{_keyPrefix}:active:{fingerprint}";

    private RedisKey UsedKey(string fingerprint) => $"{_keyPrefix}:used:{fingerprint}";

    private RedisKey DuplicateKey(string fingerprint) => $"{_keyPrefix}:duplicate:{fingerprint}";

    private RedisKey RevokedHashKey() => $"{_keyPrefix}:revoked";

    private RedisKey RevokedExpirationKey() => $"{_keyPrefix}:revoked-expiration";
}
