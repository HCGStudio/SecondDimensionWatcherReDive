using System.Text.Json.Serialization;
using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    Converters = [
        typeof(ToolEnumConverter<ManageFeedsAction>),
        typeof(ToolEnumConverter<ManageTasksAction>),
        typeof(ToolEnumConverter<QueryAnimationsAction>),
        typeof(ToolEnumConverter<QuerySeasonAction>),
        typeof(ToolEnumConverter<ManageDownloadsAction>),
        typeof(ToolEnumConverter<ToolRiskLevel>)])]
[JsonSerializable(typeof(AnimationGroupedToolResult))]
[JsonSerializable(typeof(AnimationPagedResult))]
[JsonSerializable(typeof(AnimationSearchResult))]
[JsonSerializable(typeof(FeedAddResult))]
[JsonSerializable(typeof(FeedListResult))]
[JsonSerializable(typeof(FileListResult))]
[JsonSerializable(typeof(ManageDownloadsParams))]
[JsonSerializable(typeof(ManageFeedsParams))]
[JsonSerializable(typeof(ManageTasksParams))]
[JsonSerializable(typeof(QueryAnimationsParams))]
[JsonSerializable(typeof(QueryFilesParams))]
[JsonSerializable(typeof(QuerySeasonParams))]
[JsonSerializable(typeof(SeasonListResult))]
[JsonSerializable(typeof(SubgroupListResult))]
[JsonSerializable(typeof(SubscribeBangumiParams))]
[JsonSerializable(typeof(SubscribeResult))]
[JsonSerializable(typeof(TaskListResult))]
[JsonSerializable(typeof(TaskRunResult))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ApprovalRequiredToolResult))]
internal partial class ChatToolJsonContext : JsonSerializerContext;
