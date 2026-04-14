using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<SubscribeBangumiParams>(
    "subscribe_bangumi",
    "Subscribe to a bangumi on mikanani. Requires mikan_id, optionally accepts subgroup_id.")]
internal sealed partial class SubscribeBangumiTool(
    ISeasonBangumiRepository seasonBangumiRepository,
    IFeedRepository feedRepository) : ITool
{
    private async Task<IToolExecutionResult> ExecuteCoreAsync(
        SubscribeBangumiParams param, CancellationToken cancellationToken)
    {
        var bangumi = await seasonBangumiRepository.FindByMikanIdAsync(param.MikanId, cancellationToken);
        if (bangumi is null)
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError("Bangumi not found")), false);

        var rssUrl = param.SubgroupId is not null
            ? $"https://mikanani.me/RSS/Bangumi?bangumiId={param.MikanId}&subgroupid={param.SubgroupId}"
            : $"https://mikanani.me/RSS/Bangumi?bangumiId={param.MikanId}";

        if (await feedRepository.ExistsByUrlAsync(rssUrl, cancellationToken))
            return new ToolStringResult(ChatToolHelper.Serialize(new ToolError("Already subscribed to this feed")), false);

        var feedName = bangumi.Title;
        var feed = new Feed(Guid.NewGuid(), rssUrl, feedName, DateTimeOffset.Now);
        await feedRepository.AddAsync(feed, cancellationToken);

        return new ToolStringResult(ChatToolHelper.Serialize(new SubscribeResult(true, feed.Id, feedName, rssUrl)));
    }
}
