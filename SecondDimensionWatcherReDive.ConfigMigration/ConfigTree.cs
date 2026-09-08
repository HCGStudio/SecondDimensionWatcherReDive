using System.Text.Json;
using System.Text.Json.Nodes;

namespace SecondDimensionWatcherReDive.ConfigMigration;

internal static class ConfigTree
{
    internal static JsonObject Object() => new(new JsonNodeOptions { PropertyNameCaseInsensitive = true });

    internal static JsonNode? Get(JsonObject root, string path)
    {
        JsonNode? current = root;
        foreach (var key in path.Split(':'))
        {
            if (current is not JsonObject map) return null;
            current = map[key];
        }
        return current;
    }

    internal static string? Text(JsonObject root, string path)
    {
        var node = Get(root, path);
        if (node is null) return null;
        if (node is not JsonValue value)
            throw new ConfigMigrationException($"Configuration field '{path}' must be a scalar.");
        return value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
    }

    internal static void Set(JsonObject root, string path, JsonNode? value)
    {
        var keys = path.Split(':');
        var current = root;
        foreach (var key in keys[..^1])
        {
            if (current[key] is null) current[key] = Object();
            current = current[key] as JsonObject
                      ?? throw new ConfigMigrationException($"Configuration field '{path}' conflicts with a scalar.");
        }
        current[keys[^1]] = value?.DeepClone();
    }

    internal static void Remove(JsonObject root, string path)
    {
        var keys = path.Split(':');
        var current = root;
        foreach (var key in keys[..^1])
        {
            if (current[key] is not JsonObject map) return;
            current = map;
        }
        current.Remove(keys[^1]);
    }

    internal static JsonNode? Normalize(JsonNode? node)
    {
        if (node is JsonObject map)
        {
            var result = Object();
            foreach (var pair in map)
            {
                // IConfiguration also accepts colon-delimited keys. Expand them so migration
                // and current-schema validation see the same tree as the configuration binder.
                if (Get(result, pair.Key) is not null || result.ContainsKey(pair.Key))
                    throw new ConfigMigrationException("Configuration contains duplicate or overlapping keys.");
                Set(result, pair.Key, Normalize(pair.Value));
            }
            return result;
        }
        return node is JsonArray array
            ? new JsonArray(array.Select(Normalize).ToArray())
            : node?.DeepClone();
    }

    internal static IEnumerable<KeyValuePair<string, string?>> Flatten(JsonNode? node, string prefix = "")
    {
        if (node is JsonObject map)
        {
            foreach (var pair in map)
            foreach (var value in Flatten(pair.Value, prefix.Length == 0 ? pair.Key : $"{prefix}:{pair.Key}"))
                yield return value;
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            foreach (var value in Flatten(array[i], $"{prefix}:{i}"))
                yield return value;
        }
        else if (prefix.Length > 0)
        {
            yield return new(prefix, node is JsonValue value && value.TryGetValue<string>(out var text)
                ? text : node?.ToJsonString());
        }
    }

    internal static readonly JsonDocumentOptions JsonOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
