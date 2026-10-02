namespace SecondDimensionWatcherReDive.Framework.DataRepository;

public sealed record PasskeyCredential(
    Guid Id, Guid UserId, byte[] CredentialId, byte[] PublicKey, long SignCount,
    string RelyingPartyId, string Name, bool IsBackupEligible, bool IsBackedUp,
    DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);

public sealed record PasskeyCeremony(
    Guid Id, Guid? UserId, Guid? SessionId, string Purpose, string Origin,
    string RelyingPartyId, string BrowserBindingHash, string OptionsJson, DateTimeOffset ExpiresAt);

public interface IPasskeyRepository
{
    Task<IReadOnlyList<PasskeyCredential>> GetCredentialsAsync(
        Guid userId, string? relyingPartyId, CancellationToken cancellationToken);
    Task<PasskeyCredential?> FindCredentialAsync(byte[] credentialId, CancellationToken cancellationToken);
    Task<bool> AddCredentialAsync(PasskeyCredential credential, Guid sessionId,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task AddCeremonyAsync(PasskeyCeremony ceremony, DateTimeOffset now, CancellationToken cancellationToken);
    Task<PasskeyCeremony?> ConsumeCeremonyAsync(Guid id, string purpose, Guid? sessionId,
        string origin, string browserBindingHash, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RecordAssertionAsync(PasskeyCredential credential, long signCount, bool isBackedUp,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RemovePasswordAsync(Guid userId, Guid sessionId, Guid credentialId,
        DateTimeOffset now, CancellationToken cancellationToken);
}
