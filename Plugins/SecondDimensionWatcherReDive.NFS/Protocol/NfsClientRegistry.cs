namespace SecondDimensionWatcherReDive.NFS.Protocol;

internal sealed class NfsClientRegistry
{
    private readonly object _lock = new();
    private readonly Dictionary<ulong, ClientRecord> _byClientId = new();
    private readonly Dictionary<ClientKey, ulong> _byClientKey = new();
    private ulong _nextClientId = 1;

    public ulong RegisterUnconfirmed(ulong verifier, byte[] clientIdBytes)
    {
        lock (_lock)
        {
            var key = new ClientKey(verifier, clientIdBytes);
            if (_byClientKey.TryGetValue(key, out var existing))
            {
                _byClientId[existing] = _byClientId[existing] with { LastSeen = DateTime.UtcNow };
                return existing;
            }

            var id = _nextClientId++;
            _byClientKey[key] = id;
            _byClientId[id] = new ClientRecord(id, key, false, DateTime.UtcNow);
            return id;
        }
    }

    public bool Confirm(ulong clientId)
    {
        lock (_lock)
        {
            if (!_byClientId.TryGetValue(clientId, out var record))
                return false;
            _byClientId[clientId] = record with { Confirmed = true, LastSeen = DateTime.UtcNow };
            return true;
        }
    }

    public bool Renew(ulong clientId)
    {
        lock (_lock)
        {
            if (!_byClientId.TryGetValue(clientId, out var record))
                return false;
            _byClientId[clientId] = record with { LastSeen = DateTime.UtcNow };
            return true;
        }
    }

    public bool IsConfirmed(ulong clientId)
    {
        lock (_lock)
        {
            return _byClientId.TryGetValue(clientId, out var record) && record.Confirmed;
        }
    }

    private sealed record ClientRecord(ulong ClientId, ClientKey Key, bool Confirmed, DateTime LastSeen);

    private readonly record struct ClientKey
    {
        private readonly ulong _verifier;
        private readonly byte[] _idBytes;

        public ClientKey(ulong verifier, byte[] idBytes)
        {
            _verifier = verifier;
            _idBytes = idBytes;
        }

        public bool Equals(ClientKey other) =>
            _verifier == other._verifier && _idBytes.AsSpan().SequenceEqual(other._idBytes);

        public override int GetHashCode()
        {
            var hash = _verifier.GetHashCode();
            foreach (var b in _idBytes)
                hash = HashCode.Combine(hash, b);
            return hash;
        }
    }
}
