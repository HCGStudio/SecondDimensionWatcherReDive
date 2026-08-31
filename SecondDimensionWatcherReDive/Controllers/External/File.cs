using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record FileLinkResultResponse(string Url, string? ExternalUrl = null);

internal sealed record FileLinkResultRequest([Required] Guid Id, string Path);

internal sealed record PlaybackGrant(string Path, string SessionFingerprint);

internal sealed record FileStoreListResult(string FileName, bool IsDirectory, string? Relative);
