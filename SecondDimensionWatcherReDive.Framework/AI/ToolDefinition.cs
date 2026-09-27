using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace SecondDimensionWatcherReDive.Framework.AI;

public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema,
    ToolRiskLevel RiskLevel)
{
    private static readonly JsonSchemaExporterOptions SchemaExporterOptions = new()
    {
        TreatNullObliviousAsNonNullable = true
    };

    /// <summary>
    ///     Creates a ToolDefinition by generating a JSON Schema from the given parameter type.
    /// </summary>
    public static ToolDefinition Create<TParams>(
        string name,
        string description,
        ToolRiskLevel riskLevel,
        JsonTypeInfo<TParams> parameterTypeInfo)
    {
        var schemaNode = JsonSchemaExporter.GetJsonSchemaAsNode(
            parameterTypeInfo, SchemaExporterOptions);
        var schemaElement = JsonSerializer.SerializeToElement(schemaNode, ToolSchemaJsonContext.Default.JsonNode);
        return new(name, description, schemaElement, riskLevel);
    }
}

[JsonSerializable(typeof(JsonNode))]
internal partial class ToolSchemaJsonContext : JsonSerializerContext;
