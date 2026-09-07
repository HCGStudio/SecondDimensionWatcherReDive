using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record FileLinkResultResponse(string Url, string? ExternalUrl = null);

internal sealed record FileLinkResultRequest([Required] Guid Id, string Path);

internal sealed record FileStoreToken(
    string Path,
    string FileStore,
    Guid SessionId,
    Guid UserId,
    Guid ProfileId,
    string VirtualRoot);
internal sealed record PlaybackSessionTicket(
    string UserId,
    string SessionId,
    DateTimeOffset ExpiresAt);

internal sealed record PlaybackResourceTicket(
    string Path,
    string UserId,
    string SessionId,
    DateTimeOffset ExpiresAt,
    Guid? IdentitySessionId = null,
    Guid? ProfileId = null,
    string? MappingFingerprint = null,
    string? Purpose = null);

internal sealed record FileStoreListResult(string FileName, bool IsDirectory, string? Relative);
