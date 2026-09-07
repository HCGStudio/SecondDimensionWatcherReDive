using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record TodoItemResponse(
    string Key,
    string Type,
    string Priority,
    string Title,
    string Detail,
    string DeepLink,
    Guid? ResourceId,
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset? SnoozedUntil);

internal sealed record TodoListResponse(
    IReadOnlyList<TodoItemResponse> Items,
    int TotalCount,
    int UnreadCount);

[JsonConverter(typeof(JsonStringEnumConverter<TodoStateAction>))]
internal enum TodoStateAction
{
    [JsonStringEnumMemberName("markRead")]
    MarkRead,
    [JsonStringEnumMemberName("markUnread")]
    MarkUnread,
    [JsonStringEnumMemberName("snooze")]
    Snooze,
    [JsonStringEnumMemberName("unsnooze")]
    Unsnooze
}

internal sealed record UpdateTodoStateRequest(
    [Required, MinLength(1), MaxLength(100)] IReadOnlyList<string> Keys,
    [Required] TodoStateAction? Action,
    DateTimeOffset? SnoozedUntil);
