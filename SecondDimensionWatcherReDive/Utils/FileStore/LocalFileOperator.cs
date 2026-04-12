using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class LocalFileOperator(ILogger<LocalFileOperator> logger) : IFileOperator
{
    public Task<bool> Rename(string oldName, string newName)
    {
        try
        {
            File.Move(oldName, newName);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to rename file from {OldName} to {NewName}", oldName, newName);
            return Task.FromResult(false);
        }
    }
}
