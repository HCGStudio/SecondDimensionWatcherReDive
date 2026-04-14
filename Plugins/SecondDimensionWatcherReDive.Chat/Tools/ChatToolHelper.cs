using System.Text.Json;
using SecondDimensionWatcherReDive.Framework.DataRepository;

namespace SecondDimensionWatcherReDive.Chat.Tools;

internal static class ChatToolHelper
{
    internal static AnimationSummary ToSummary(AnimationInfo info) => new(
        info.Id, info.Title, info.Season, info.Episode,
        info.IsDownloadTracked, info.IsDownloadFinished, info.IsAiProcessed,
        info.Animation?.Name, info.Group?.Name, info.PublishTime);

    internal static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, typeof(T), ChatToolJsonSerializerContext.Default);
}
