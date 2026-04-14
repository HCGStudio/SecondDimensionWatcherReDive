using System.ComponentModel.DataAnnotations;

namespace SecondDimensionWatcherReDive.Controllers.External;

internal sealed record FileLinkResultResponse(string Url);

internal sealed record FileLinkResultRequest([Required] Guid Id, string Path);

internal sealed record FileStoreToken(string Path, string FileStore);

internal sealed record FileStoreListResult(string FileName, bool IsDirectory, string? Relative);
