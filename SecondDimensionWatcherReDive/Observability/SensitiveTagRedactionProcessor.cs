using System.Diagnostics;
using OpenTelemetry;

namespace SecondDimensionWatcherReDive.Observability;

internal sealed class SensitiveTagRedactionProcessor : BaseProcessor<Activity>
{
    private static readonly HashSet<string> RemovedTags = new(StringComparer.Ordinal)
    {
        "db.statement",
        "db.query.text",
        "url.full",
        "url.path",
        "url.query",
        "http.url",
        "http.target",
        "tool.arguments",
        "tool.result"
    };

    public override void OnEnd(Activity activity)
    {
        foreach (var key in activity.TagObjects
                     .Select(pair => pair.Key)
                     .Where(ShouldRemove)
                     .ToArray())
            activity.SetTag(key, null);

        if (activity.Source.Name.Contains("EntityFrameworkCore", StringComparison.Ordinal))
            activity.DisplayName = "database.query";
        else if (activity.Source.Name.Contains("HttpClient", StringComparison.Ordinal))
            activity.DisplayName = $"HTTP {GetTag(activity, "http.request.method") ?? "request"}";
        else if (activity.Source.Name.Contains("AspNetCore", StringComparison.Ordinal))
        {
            var method = GetTag(activity, "http.request.method") ?? "request";
            var route = GetTag(activity, "http.route");
            activity.DisplayName = route is null ? $"HTTP {method}" : $"{method} {route}";
        }
    }

    private static string? GetTag(Activity activity, string key) =>
        activity.GetTagItem(key)?.ToString();

    private static bool ShouldRemove(string key) =>
        RemovedTags.Contains(key)
        || key.StartsWith("db.query.parameter.", StringComparison.Ordinal)
        || key.StartsWith("tool.argument", StringComparison.Ordinal)
        || key.StartsWith("tool.result", StringComparison.Ordinal);
}
