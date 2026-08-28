using Microsoft.Extensions.Options;

namespace SecondDimensionWatcherReDive.Test;

internal sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
{
    private event Action<TOptions, string?>? Changed;

    public TOptions CurrentValue { get; private set; } = currentValue;

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        Changed += listener;
        return new Subscription(() => Changed -= listener);
    }

    public void Set(TOptions value, string? name = null)
    {
        CurrentValue = value;
        Changed?.Invoke(value, name);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
