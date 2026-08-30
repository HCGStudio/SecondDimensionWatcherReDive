using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<ManageFeedsParams>(
    "manage_feeds",
    "Manage RSS feed subscriptions. Supports listing all feeds, adding new feeds, and removing feeds.",
    ToolRiskLevel.Destructive)]
internal sealed partial class ManageFeedsTool(
    IFeedRepository feedRepository) : ITool
{
    private async Task<IToolResult> ExecuteCoreAsync(
        ManageFeedsParams param, CancellationToken cancellationToken)
    {
        return param.Action switch
        {
            ManageFeedsAction.List => new ToolSuccessResult<FeedListResult>(
                await ListFeedsAsync(cancellationToken)),
            ManageFeedsAction.Add => await AddFeedAsync(param, cancellationToken),
            ManageFeedsAction.Remove => await RemoveFeedAsync(param, cancellationToken),
            _ => new ToolFailureResult($"Unknown action: {param.Action}")
        };
    }

    private async Task<FeedListResult> ListFeedsAsync(CancellationToken cancellationToken)
    {
        var feeds = await feedRepository.GetAllOrderedAsync(cancellationToken);
        return new FeedListResult(
            feeds.Count,
            feeds.Select(f => new FeedSummary(f.Id, f.Url, f.Name, f.CreatedAt)));
    }

    private async Task<IToolResult> AddFeedAsync(ManageFeedsParams param, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(param.Url))
            return new ToolFailureResult("url is required");

        if (await feedRepository.ExistsByUrlAsync(param.Url, cancellationToken))
            return new ToolFailureResult("Feed with this URL already exists");

        var feed = new Feed(Guid.NewGuid(), param.Url, param.Name, DateTimeOffset.Now);
        await feedRepository.AddAsync(feed, cancellationToken);
        return new ToolSuccessResult<FeedAddResult>(new FeedAddResult(true, feed.Id, feed.Url, feed.Name));
    }

    private async Task<IToolResult> RemoveFeedAsync(ManageFeedsParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.Id, out var id))
            return new ToolFailureResult("Invalid or missing id");

        var feed = await feedRepository.FindByIdAsync(id, cancellationToken);
        if (feed is null)
            return new ToolFailureResult("Feed not found");

        await feedRepository.RemoveAsync(feed, cancellationToken);
        return new ToolSuccessResult<string>("Feed removed");
    }
}

internal enum ManageFeedsAction
{
    List,
    Add,
    Remove
}

internal sealed record ManageFeedsParams(
    ManageFeedsAction Action,
    string? Url = null,
    string? Name = null,
    string? Id = null);

internal sealed record FeedListResult(int Count, IEnumerable<FeedSummary> Feeds);
internal sealed record FeedSummary(Guid Id, string Url, string? Name, DateTimeOffset CreatedAt);
internal sealed record FeedAddResult(bool Success, Guid Id, string Url, string? Name);
