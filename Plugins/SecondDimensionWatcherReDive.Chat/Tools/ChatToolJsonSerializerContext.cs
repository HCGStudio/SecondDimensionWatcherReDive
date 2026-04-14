using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ToolError))]
[JsonSerializable(typeof(ToolSuccess))]
[JsonSerializable(typeof(AnimationPagedResult))]
[JsonSerializable(typeof(AnimationSummary))]
[JsonSerializable(typeof(AnimationGroupedToolResult))]
[JsonSerializable(typeof(AnimationGroupItem))]
[JsonSerializable(typeof(AnimationSearchResult))]
[JsonSerializable(typeof(FeedListResult))]
[JsonSerializable(typeof(FeedSummary))]
[JsonSerializable(typeof(FeedAddResult))]
[JsonSerializable(typeof(SeasonListResult))]
[JsonSerializable(typeof(BangumiSummary))]
[JsonSerializable(typeof(SubgroupListResult))]
[JsonSerializable(typeof(SubgroupSummary))]
[JsonSerializable(typeof(SubscribeResult))]
[JsonSerializable(typeof(TaskListResult))]
[JsonSerializable(typeof(TaskSummary))]
[JsonSerializable(typeof(TaskRunResult))]
[JsonSerializable(typeof(FileListResult))]
[JsonSerializable(typeof(FileSummary))]
internal partial class ChatToolJsonSerializerContext : JsonSerializerContext;
