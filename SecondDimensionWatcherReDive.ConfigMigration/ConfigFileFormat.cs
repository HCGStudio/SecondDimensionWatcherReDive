using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace SecondDimensionWatcherReDive.ConfigMigration;

internal static class ConfigFileFormat
{
    internal static JsonObject Read(string text, string path)
    {
        try
        {
            JsonNode? node;
            if (IsYaml(path))
            {
                var yaml = new YamlStream();
                yaml.Load(new StringReader(text));
                if (yaml.Documents.Count != 1)
                    throw new ConfigMigrationException("Configuration must contain exactly one YAML document.");
                node = ReadYaml(yaml.Documents[0].RootNode, 0);
            }
            else
                node = JsonNode.Parse(text, documentOptions: ConfigTree.JsonOptions);
            return ConfigTree.Normalize(node) as JsonObject
                   ?? throw new ConfigMigrationException("Configuration must have an object at its root.");
        }
        catch (Exception exception) when (exception is JsonException or YamlException or ArgumentException)
        {
            // Parser messages can contain credentials from the input document.
            throw new ConfigMigrationException($"Cannot parse configuration '{path}'; check its syntax and duplicate keys.");
        }
    }

    internal static string Write(JsonObject configuration, string path)
    {
        if (!IsYaml(path))
            return configuration.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        var stream = new YamlStream(new YamlDocument(WriteYaml(configuration)));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    // External Config historically used the YAML provider regardless of suffix,
    // including mounted secrets with no extension. Only explicit .json selects JSON.
    private static bool IsYaml(string path) => !Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);

    private static JsonNode? ReadYaml(YamlNode node, int depth)
    {
        if (depth > 64) throw new ConfigMigrationException("Configuration is too deeply nested or contains a YAML alias cycle.");
        if (node is YamlMappingNode map)
        {
            var result = ConfigTree.Object();
            foreach (var pair in map.Children)
            {
                if (pair.Key is not YamlScalarNode { Value: { } key } || string.IsNullOrWhiteSpace(key))
                    throw new ConfigMigrationException("Configuration keys must be nonempty strings.");
                if (result.ContainsKey(key))
                    throw new ConfigMigrationException("Configuration contains duplicate keys.");
                result.Add(key, ReadYaml(pair.Value, depth + 1));
            }
            return result;
        }
        if (node is YamlSequenceNode sequence)
            return new JsonArray(sequence.Children.Select(child => ReadYaml(child, depth + 1)).ToArray());
        if (node is not YamlScalarNode scalar)
            throw new ConfigMigrationException("Unsupported YAML configuration node.");
        var text = scalar.Value;
        if (scalar.Style == ScalarStyle.Plain)
        {
            if (text is null or "" or "~" or "null" or "Null" or "NULL") return null;
            if (bool.TryParse(text, out var boolean)) return JsonValue.Create(boolean);
            // Keep strings (including secrets and URLs) exact. JSON numbers retain their
            // type across YAML/JSON serialization without culture-sensitive conversion.
            if (text.Length > 0 && (char.IsDigit(text[0]) || text[0] == '-'))
            {
                try
                {
                    var value = JsonNode.Parse(text);
                    if (value is JsonValue && value.GetValueKind() == JsonValueKind.Number) return value;
                }
                catch (JsonException) { }
            }
        }
        return JsonValue.Create(text ?? "");
    }

    private static YamlNode WriteYaml(JsonNode? node)
    {
        if (node is JsonObject map)
        {
            var result = new YamlMappingNode();
            foreach (var pair in map) result.Add(new YamlScalarNode(pair.Key), WriteYaml(pair.Value));
            return result;
        }
        if (node is JsonArray array) return new YamlSequenceNode(array.Select(WriteYaml));
        if (node is JsonValue value && value.TryGetValue<string>(out var text))
            return new YamlScalarNode(text) { Style = ScalarStyle.DoubleQuoted };
        return new YamlScalarNode(node?.ToJsonString() ?? "null") { Style = ScalarStyle.Plain };
    }
}
