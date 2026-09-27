using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SecondDimensionWatcherReDive.Analyzers;

[Generator]
public sealed class ToolGenerator : IIncrementalGenerator
{
    private const string ToolAttributeMetadataName = "SecondDimensionWatcherReDive.Framework.Attributes.ToolAttribute`1";

    private static readonly DiagnosticDescriptor MissingMetadata = new(
        "SDWTOOL001", "Tool JSON metadata must be source generated",
        "Tool '{0}' must provide a JsonSerializerContext with [JsonSerializable] metadata for '{1}'",
        "Tools", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var toolClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ToolAttributeMetadataName,
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetToolInfo(ctx, ct))
            .Where(static info => info is not null);

        context.RegisterSourceOutput(toolClasses, static (spc, info) =>
        {
            if (info!.Value.MissingMetadataType is { } missingType)
            {
                spc.ReportDiagnostic(Diagnostic.Create(MissingMetadata, info.Value.Location,
                    info.Value.ClassName, missingType));
                return;
            }
            Execute(spc, info.Value);
        });
    }

    private static ToolInfo? GetToolInfo(GeneratorAttributeSyntaxContext context, System.Threading.CancellationToken ct)
    {
        var classSymbol = (INamedTypeSymbol)context.TargetSymbol;

        // Validate partial
        var classDecl = (ClassDeclarationSyntax)context.TargetNode;
        var isPartial = false;
        foreach (var modifier in classDecl.Modifiers)
        {
            if (modifier.Text == "partial")
            {
                isPartial = true;
                break;
            }
        }

        if (!isPartial)
            return null;

        // Get the attribute data
        var attributeData = context.Attributes[0];
        var attributeClass = attributeData.AttributeClass;
        if (attributeClass is null || !attributeClass.IsGenericType || attributeClass.TypeArguments.Length != 1)
            return null;

        var paramType = attributeClass.TypeArguments[0];
        var paramTypeFqn = paramType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // The context must exist in authored source so the System.Text.Json generator can see it.
        if (attributeData.ConstructorArguments.Length < 4)
            return null;

        var toolName = attributeData.ConstructorArguments[0].Value as string;
        var toolDescription = attributeData.ConstructorArguments[1].Value as string;
        var riskLevel = attributeData.ConstructorArguments[2].Value as int?;
        if (toolName is null || toolDescription is null || riskLevel is null)
            return null;

        // Validate ExecuteCoreAsync method exists
        var hasExecuteCore = false;
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is IMethodSymbol method
                && method.Name == "ExecuteCoreAsync"
                && method.Parameters.Length == 2
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, paramType)
                && method.Parameters[1].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                   "global::System.Threading.CancellationToken")
            {
                hasExecuteCore = true;
                break;
            }
        }

        if (!hasExecuteCore)
            return null;

        var namespaceName = classSymbol.ContainingNamespace.IsGlobalNamespace
            ? null
            : classSymbol.ContainingNamespace.ToDisplayString();

        var contextType = attributeData.ConstructorArguments[3].Value as INamedTypeSymbol;
        var missingMetadataType = HasMetadata(contextType, paramType) ? null : paramTypeFqn;
        foreach (var syntaxReference in classSymbol.DeclaringSyntaxReferences)
        {
            var declaration = syntaxReference.GetSyntax(ct);
            var model = context.SemanticModel.Compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (var node in declaration.DescendantNodes())
            {
                if (node is GenericNameSyntax genericName && genericName.Identifier.ValueText == "Success"
                    && genericName.TypeArgumentList.Arguments.Count == 1)
                {
                    var resultType = model.GetTypeInfo(genericName.TypeArgumentList.Arguments[0], ct).Type;
                    if (resultType is not null && !HasMetadata(contextType, resultType))
                        missingMetadataType = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }
            }
        }

        return new ToolInfo
        {
            Namespace = namespaceName,
            ClassName = classSymbol.Name,
            ParamTypeFqn = paramTypeFqn,
            ToolName = toolName,
            ToolDescription = toolDescription,
            RiskLevel = riskLevel.Value,
            ContextTypeFqn = contextType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
            MissingMetadataType = missingMetadataType,
            Location = classDecl.Identifier.GetLocation()
        };
    }

    private static bool HasMetadata(INamedTypeSymbol? contextType, ITypeSymbol type)
    {
        if (contextType is null) return false;
        var baseType = contextType.BaseType;
        while (baseType is not null
               && baseType.ToDisplayString() != "System.Text.Json.Serialization.JsonSerializerContext")
            baseType = baseType.BaseType;
        if (baseType is null) return false;

        foreach (var attribute in contextType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "System.Text.Json.Serialization.JsonSerializableAttribute"
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is ITypeSymbol registeredType
                && SymbolEqualityComparer.Default.Equals(registeredType, type))
                return true;
        }
        return false;
    }

    private static void Execute(SourceProductionContext context, ToolInfo info)
    {
        var escapedName = EscapeStringLiteral(info.ToolName);
        var escapedDescription = EscapeStringLiteral(info.ToolDescription);

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        if (info.Namespace is not null)
        {
            sb.Append("namespace ");
            sb.Append(info.Namespace);
            sb.AppendLine(";");
            sb.AppendLine();
        }

        sb.Append("partial class ");
        sb.AppendLine(info.ClassName);
        sb.AppendLine("{");

        sb.Append("    private static readonly global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<");
        sb.Append(info.ParamTypeFqn);
        sb.Append("> ParameterTypeInfo = (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<");
        sb.Append(info.ParamTypeFqn);
        sb.Append(">)");
        sb.Append(info.ContextTypeFqn);
        sb.Append(".Default.GetTypeInfo(typeof(");
        sb.Append(info.ParamTypeFqn);
        sb.AppendLine("))!;");
        sb.AppendLine();

        // Generate static Definition property
        sb.AppendLine("    public static global::SecondDimensionWatcherReDive.Framework.AI.ToolDefinition Definition { get; } =");
        sb.Append("        global::SecondDimensionWatcherReDive.Framework.AI.ToolDefinition.Create<");
        sb.Append(info.ParamTypeFqn);
        sb.AppendLine(">(");
        sb.Append("            \"");
        sb.Append(escapedName);
        sb.Append("\", \"");
        sb.Append(escapedDescription);
        sb.AppendLine("\",");
        sb.Append("            (global::SecondDimensionWatcherReDive.Framework.AI.ToolRiskLevel)");
        sb.Append(info.RiskLevel);
        sb.AppendLine(", ParameterTypeInfo);");
        sb.AppendLine();

        // Generate ExecuteAsync method — param deserialization only, no result serialization
        sb.AppendLine("    public async global::System.Threading.Tasks.Task<global::SecondDimensionWatcherReDive.Framework.AI.IToolResult> ExecuteAsync(");
        sb.AppendLine("        global::System.Text.Json.JsonElement arguments,");
        sb.AppendLine("        global::System.Threading.CancellationToken cancellationToken)");
        sb.AppendLine("    {");
        sb.Append("        var param = global::System.Text.Json.JsonSerializer.Deserialize<");
        sb.Append(info.ParamTypeFqn);
        sb.AppendLine(">(");
        sb.AppendLine("            arguments,");
        sb.AppendLine("            ParameterTypeInfo);");
        sb.AppendLine("        if (param is null)");
        sb.Append("            return new global::SecondDimensionWatcherReDive.AI.Models.ToolFailureResult(\"Failed to deserialize parameters for tool '");
        sb.Append(escapedName);
        sb.AppendLine("'\");");
        sb.AppendLine("        return await ExecuteCoreAsync(param, cancellationToken);");
        sb.AppendLine("    }");

        sb.AppendLine();
        sb.AppendLine("    private static global::SecondDimensionWatcherReDive.AI.Models.ToolSuccessResult<T> Success<T>(T result) =>");
        sb.AppendLine("        new(result, (global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>?)");
        sb.Append("            ");
        sb.Append(info.ContextTypeFqn);
        sb.AppendLine(".Default.GetTypeInfo(typeof(T))");
        sb.AppendLine("            ?? throw new global::System.NotSupportedException($\"Tool result '{typeof(T)}' requires source-generated JSON metadata.\"));");
        sb.AppendLine("}");

        var hintName = info.Namespace is not null
            ? $"{info.Namespace}.{info.ClassName}.g.cs"
            : $"{info.ClassName}.g.cs";

        context.AddSource(hintName, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string EscapeStringLiteral(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    private struct ToolInfo
    {
        public string? Namespace;
        public string ClassName;
        public string ParamTypeFqn;
        public string ToolName;
        public string ToolDescription;
        public int RiskLevel;
        public string ContextTypeFqn;
        public string? MissingMetadataType;
        public Location Location;
    }
}
