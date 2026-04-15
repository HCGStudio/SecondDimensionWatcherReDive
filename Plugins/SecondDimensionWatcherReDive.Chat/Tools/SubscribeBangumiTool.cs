using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<SubscribeBangumiParams>(
    "subscribe_bangumi",
    "Subscribe to a bangumi on mikanani. Requires mikan_id, optionally accepts subgroup_id.")]
internal sealed partial class SubscribeBangumiTool(
    ISeasonBangumiRepository seasonBangumiRepository,
    IFeedRepository feedRepository) : ITool
{
    private async Task<IToolResult> ExecuteCoreAsync(
        SubscribeBangumiParams param, CancellationToken cancellationToken)
    {
        var bangumi = await seasonBangumiRepository.FindByMikanIdAsync(param.MikanId, cancellationToken);
        if (bangumi is null)
            return new ToolFailureResult("Bangumi not found");

        var rssUrl = param.SubgroupId is not null
            ? $"https://mikanani.me/RSS/Bangumi?bangumiId={param.MikanId}&subgroupid={param.SubgroupId}"
            : $"https://mikanani.me/RSS/Bangumi?bangumiId={param.MikanId}";

        if (await feedRepository.ExistsByUrlAsync(rssUrl, cancellationToken))
            return new ToolFailureResult("Already subscribed to this feed");

        var feedName = bangumi.Title;
        var feed = new Feed(Guid.NewGuid(), rssUrl, feedName, DateTimeOffset.Now);
        await feedRepository.AddAsync(feed, cancellationToken);

        return new ToolSuccessResult<SubscribeResult>(new SubscribeResult(true, feed.Id, feedName, rssUrl));
    }
}

internal sealed record SubscribeBangumiParams(
    int MikanId,
    int? SubgroupId = null);

internal sealed record SubscribeResult(bool Success, Guid FeedId, string FeedName, string RssUrl);
