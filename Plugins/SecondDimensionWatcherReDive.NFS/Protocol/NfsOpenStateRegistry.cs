namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal sealed class NfsOpenStateRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<(ulong Hi, uint Lo), OpenState> _states = new();
    private ulong _nextOtherHi = 1;

    public NfsStateId Allocate(ulong clientId, byte[] handleBytes)
    {
        lock (_lock)
        {
            var hi = _nextOtherHi++;
            var lo = unchecked((uint)clientId);
            _states[(hi, lo)] = new OpenState(clientId, handleBytes, 1);
            return new NfsStateId(1, hi, lo);
        }
    }

    public NfsStateId Confirm(NfsStateId stateId)
    {
        lock (_lock)
        {
            var key = (stateId.OtherHi, stateId.OtherLo);
            if (!_states.TryGetValue(key, out var state))
                return stateId;
            var bumped = state with { SeqId = state.SeqId + 1 };
            _states[key] = bumped;
            return new NfsStateId(bumped.SeqId, stateId.OtherHi, stateId.OtherLo);
        }
    }

    public bool Validate(NfsStateId stateId)
    {
        if (stateId.IsAny)
            return true;
        lock (_lock)
        {
            return _states.ContainsKey((stateId.OtherHi, stateId.OtherLo));
        }
    }

    public bool Close(NfsStateId stateId)
    {
        lock (_lock)
        {
            return _states.Remove((stateId.OtherHi, stateId.OtherLo));
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
                return _states.Count;
        }
    }

    private sealed record OpenState(ulong ClientId, byte[] HandleBytes, uint SeqId);
}
