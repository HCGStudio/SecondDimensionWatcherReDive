using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.Framework.AI;

namespace SecondDimensionWatcherReDive.AI.Models;

public sealed class ToolExecutorBuilder(IServiceProvider serviceProvider) : IToolExecutorBuilder
{
    private readonly List<ToolRegistration> _tools = [];

    public IToolExecutorBuilder AddTool<TTool>() where TTool : class, ITool
    {
        _tools.Add(new(typeof(TTool), TTool.Definition));
        return this;
    }

    public IToolExecutor Build()
    {
        var executors = new Dictionary<string, Func<JsonElement, CancellationToken, Task<IToolResult>>>(_tools.Count);
        var definitions = new List<ToolDefinition>(_tools.Count);

        foreach (var registration in _tools)
        {
            var tool = serviceProvider.GetRequiredService(registration.ToolType);
            executors[registration.Definition.Name] = ((ITool)tool).ExecuteAsync;
            definitions.Add(registration.Definition);
        }

        return new DefaultToolExecutor(executors, definitions);
    }

    private sealed record ToolRegistration(Type ToolType, ToolDefinition Definition);
}

internal sealed class DefaultToolExecutor(
    Dictionary<string, Func<JsonElement, CancellationToken, Task<IToolResult>>> executors,
    List<ToolDefinition> definitions) : IToolExecutor
{
    public IReadOnlyList<ToolDefinition> ToolDefinitions => definitions;

    public async Task<IToolResult> ExecuteAsync(ToolCall toolCall, CancellationToken cancellationToken)
    {
        if (!executors.TryGetValue(toolCall.Name, out var execute))
            return new ToolFailureResult($"Unknown tool: {toolCall.Name}");

        try
        {
            using var document = JsonDocument.Parse(toolCall.Arguments);
            return await execute(document.RootElement, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ToolFailureResult($"Tool '{toolCall.Name}' failed: {ex.Message}");
        }
    }
}
