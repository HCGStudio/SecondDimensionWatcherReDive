using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.IntegrationTest.Helpers;

internal sealed class FakeWebDavTokenRepository : IWebDavTokenRepository
{
    private readonly WebDavToken _seeded;

    public FakeWebDavTokenRepository(string username, string tokenHash)
    {
        _seeded = new WebDavToken(Guid.NewGuid(), username, tokenHash, "integration-test", DateTimeOffset.UtcNow);
    }

    public Task<IReadOnlyList<WebDavToken>> GetAllOrderedAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<WebDavToken>>(new[] { _seeded });

    public Task<WebDavToken?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
        => Task.FromResult<WebDavToken?>(username == _seeded.Username ? _seeded : null);

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken)
        => Task.FromResult(username == _seeded.Username);

    public Task AddAsync(WebDavToken token, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<bool> RemoveByIdAsync(Guid id, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
