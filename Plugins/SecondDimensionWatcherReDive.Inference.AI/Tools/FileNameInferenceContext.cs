using System.Threading;
using SecondDimensionWatcherReDive.Framework.Inference;

namespace SecondDimensionWatcherReDive.Inference.AI.Tools;

public sealed class FileNameInferenceContext
{
    private readonly AsyncLocal<FileNameInferenceRequest?> _current = new();

    public FileNameInferenceRequest? Current => _current.Value;

    public IDisposable Push(FileNameInferenceRequest request)
    {
        var previous = _current.Value;
        _current.Value = request;
        return new Scope(() => _current.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _dispose, null)?.Invoke();
        }
    }
}
