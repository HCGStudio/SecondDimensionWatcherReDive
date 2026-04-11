namespace SecondDimensionWatcherReDive.Framework.FileStore;

public interface IFileOperator
{
    public Task<bool> Rename(string oldName, string newName);
}
