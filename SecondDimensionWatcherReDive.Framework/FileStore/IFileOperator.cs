namespace SecondDimensionWatcherReDive.Framework.FileStore;

public interface IFileOperator
{
    public Task<bool> RenameAsync(string oldName, string newName, CancellationToken cancellationToken);
}
