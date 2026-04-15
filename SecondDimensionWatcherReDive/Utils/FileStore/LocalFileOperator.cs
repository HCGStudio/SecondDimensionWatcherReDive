using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public partial class LocalFileOperator(ILogger<LocalFileOperator> logger) : IFileOperator
{
    public Task<bool> RenameAsync(string oldName, string newName, CancellationToken cancellationToken)
    {
        try
        {
            File.Move(oldName, newName);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            LogRenameFileFailed(logger, ex, oldName, newName);
            return Task.FromResult(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to rename file from {OldName} to {NewName}")]
    private static partial void LogRenameFileFailed(ILogger logger, Exception ex, string oldName, string newName);
}
