using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<ManageFeedsParams>(
    "manage_feeds",
    "Manage RSS feed subscriptions. Supports listing all feeds, adding new feeds, and removing feeds.")]
internal sealed partial class ManageFeedsTool(
    IFeedRepository feedRepository) : ITool
{
    private async Task<IToolExecutionResult> ExecuteCoreAsync(
        ManageFeedsParams param, CancellationToken cancellationToken)
    {
        var result = param.Action switch
        {
            ManageFeedsAction.List => await ListFeedsAsync(cancellationToken),
            ManageFeedsAction.Add => await AddFeedAsync(param, cancellationToken),
            ManageFeedsAction.Remove => await RemoveFeedAsync(param, cancellationToken),
            _ => ChatToolHelper.Serialize(new ToolError($"Unknown action: {param.Action}"))
        };
        return new ToolStringResult(result);
    }

    private async Task<string> ListFeedsAsync(CancellationToken cancellationToken)
    {
        var feeds = await feedRepository.GetAllOrderedAsync(cancellationToken);
        return ChatToolHelper.Serialize(new FeedListResult(
            feeds.Count,
            feeds.Select(f => new FeedSummary(f.Id, f.Url, f.Name, f.CreatedAt))));
    }

    private async Task<string> AddFeedAsync(ManageFeedsParams param, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(param.Url))
            return ChatToolHelper.Serialize(new ToolError("url is required"));

        if (await feedRepository.ExistsByUrlAsync(param.Url, cancellationToken))
            return ChatToolHelper.Serialize(new ToolError("Feed with this URL already exists"));

        var feed = new Feed(Guid.NewGuid(), param.Url, param.Name, DateTimeOffset.Now);
        await feedRepository.AddAsync(feed, cancellationToken);
        return ChatToolHelper.Serialize(new FeedAddResult(true, feed.Id, feed.Url, feed.Name));
    }

    private async Task<string> RemoveFeedAsync(ManageFeedsParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.Id, out var id))
            return ChatToolHelper.Serialize(new ToolError("Invalid or missing id"));

        var feed = await feedRepository.FindByIdAsync(id, cancellationToken);
        if (feed is null)
            return ChatToolHelper.Serialize(new ToolError("Feed not found"));

        await feedRepository.RemoveAsync(feed, cancellationToken);
        return ChatToolHelper.Serialize(new ToolSuccess(true));
    }
}
