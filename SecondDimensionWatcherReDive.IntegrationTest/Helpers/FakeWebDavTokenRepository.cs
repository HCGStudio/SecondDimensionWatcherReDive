using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.IntegrationTest.Helpers;

internal sealed class FakeWebDavTokenRepository : IWebDavTokenRepository
{
    private WebDavToken _seeded;

    public Guid TokenId => _seeded.Id;

    public void Expire() => _seeded = _seeded with
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
    };

    public void SetScope(string scope) => _seeded = _seeded with { Scope = scope };

    public FakeWebDavTokenRepository(
        Guid userId,
        string username,
        string tokenHash,
        string virtualRoot = "/")
    {
        _seeded = new WebDavToken(
            Guid.NewGuid(),
            userId,
            username,
            tokenHash,
            "integration-test",
            DateTimeOffset.UtcNow,
            "read",
            virtualRoot,
            DateTimeOffset.UtcNow.AddDays(1),
            null);
    }

    public Task<IReadOnlyList<WebDavToken>> GetAllOrderedAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<WebDavToken>>(new[] { _seeded });

    public Task<WebDavToken?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
        => Task.FromResult<WebDavToken?>(username == _seeded.Username ? _seeded : null);

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken)
        => Task.FromResult(username == _seeded.Username);

    public Task AddAsync(WebDavToken token, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<bool> RevokeByIdAsync(
        Guid id,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        if (_seeded.Id != id) return Task.FromResult(false);
        _seeded = _seeded with { RevokedAt = revokedAt };
        return Task.FromResult(true);
    }
}
