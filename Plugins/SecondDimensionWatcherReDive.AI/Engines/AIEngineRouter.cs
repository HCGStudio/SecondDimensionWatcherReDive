using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.AI.Abstractions;
using SecondDimensionWatcherReDive.AI.Configuration;
using SecondDimensionWatcherReDive.AI.Models;

namespace SecondDimensionWatcherReDive.AI.Engines;

public sealed class AIEngineRouter(
    IEnumerable<IAIEngineBackend> backends,
    IOptionsMonitor<AIOptions> options) : IAIEngine, IAIEngineStatus
{
    private readonly IReadOnlyDictionary<AIEngineKind, IAIEngineBackend> _backends =
        backends.ToDictionary(backend => backend.Kind);

    public string Name => GetCurrentBackend().Name;

    public bool IsConfigured => GetCurrentBackend().IsConfigured;

    public Task<IReadOnlyList<AIModel>> GetAvailableModelsAsync(CancellationToken cancellationToken)
        => GetConfiguredBackend().GetAvailableModelsAsync(cancellationToken);

    public async IAsyncEnumerable<IChatUpdate> ChatAsync(
        IReadOnlyList<IMessage> messages,
        ChatOptions? chatOptions,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var backend = GetConfiguredBackend();
        await foreach (var update in backend.ChatAsync(messages, chatOptions, cancellationToken))
            yield return update;
    }

    private IAIEngineBackend GetConfiguredBackend()
    {
        var backend = GetCurrentBackend();
        if (!backend.IsConfigured)
            throw new InvalidOperationException($"AI engine '{backend.Name}' is not configured.");
        return backend;
    }

    private IAIEngineBackend GetCurrentBackend()
    {
        var kind = options.CurrentValue.Engine;
        if (_backends.TryGetValue(kind, out var backend))
            return backend;

        throw new InvalidOperationException($"AI engine '{kind}' is not registered.");
    }
}
