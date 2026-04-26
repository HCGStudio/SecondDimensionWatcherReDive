namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public interface IWebDavTokenRepository
{
    Task<IReadOnlyList<WebDavToken>> GetAllOrderedAsync(CancellationToken cancellationToken);

    Task<WebDavToken?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<bool> ExistsByUsernameAsync(string username, CancellationToken cancellationToken);

    Task AddAsync(WebDavToken token, CancellationToken cancellationToken);

    Task<bool> RemoveByIdAsync(Guid id, CancellationToken cancellationToken);
}
