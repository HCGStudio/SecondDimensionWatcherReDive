using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed record ToolDefinition(string Name, string Description, JsonElement ParametersSchema)
{
    private static readonly JsonSerializerOptions SchemaSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly JsonSchemaExporterOptions SchemaExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true
    };

    /// <summary>
    ///     Creates a ToolDefinition by generating a JSON Schema from the given parameter type.
    /// </summary>
    public static ToolDefinition Create<TParams>(string name, string description)
    {
        var schemaNode = JsonSchemaExporter.GetJsonSchemaAsNode(
            SchemaSerializerOptions, typeof(TParams), SchemaExporterOptions);
        var schemaElement = JsonSerializer.Deserialize<JsonElement>(schemaNode.ToJsonString());
        return new(name, description, schemaElement);
    }
}
