using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class LocalFileOperator : IFileOperator
{
    public Task<bool> Rename(string oldName, string newName)
    {
        try
        {
            File.Move(oldName, newName);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
