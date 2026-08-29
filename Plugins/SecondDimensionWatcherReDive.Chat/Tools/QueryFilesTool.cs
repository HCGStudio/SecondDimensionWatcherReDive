using SecondDimensionWatcherReDive.AI.Models;
using SecondDimensionWatcherReDive.Framework.AI;
using SecondDimensionWatcherReDive.Framework.Attributes;
using SecondDimensionWatcherReDive.Framework.DataRepository;
using SecondDimensionWatcherReDive.Framework.FileStore;

namespace SecondDimensionWatcherReDive.Chat.Tools;

[Tool<QueryFilesParams>(
    "query_files",
    "Query the file list of downloaded animations. Supports browsing subdirectories.",
    ToolRiskLevel.ReadOnly)]
internal sealed partial class QueryFilesTool(
    IAnimationInfoRepository animationInfoRepository,
    IFileExplorer fileExplorer) : ITool
{
    private async Task<IToolResult> ExecuteCoreAsync(
        QueryFilesParams param, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(param.AnimationId, out var animationId))
            return new ToolFailureResult("Invalid or missing animation_id");

        var info = await animationInfoRepository.FindByIdWithAnimationAsync(animationId, cancellationToken);
        if (info is null)
            return new ToolFailureResult("Animation not found");

        if (!info.IsDownloadFinished)
            return new ToolFailureResult("Download not finished");

        var root = GetAnimationVirtualRoot(info);
        var virtualPath = string.IsNullOrWhiteSpace(param.RelativeDir)
            ? root
            : $"{root}/{param.RelativeDir.Trim('/')}";

        var tokens = await fileExplorer.EnumerateDirectoryAsync(
            new DirectoryToken(virtualPath, Path.GetFileName(virtualPath.TrimEnd('/'))),
            cancellationToken);

        var files = tokens.Select(t => t switch
        {
            FileToken f => new FileSummary(f.FileName, false, f.Path),
            DirectoryToken d => new FileSummary(d.FileName, true, d.Path),
            _ => throw new InvalidOperationException()
        }).ToList();

        return new ToolSuccessResult<FileListResult>(new FileListResult(info.Title, virtualPath, files));
    }

    private static string GetAnimationVirtualRoot(AnimationInfo info)
    {
        if (info.Animation is null || info.Season is null) return "/unknown";
        var animationName = SanitizePathSegment(info.Animation.Name);
        var subGroup = SanitizePathSegment(info.Group?.Name ?? "Unknown");
        return $"/{animationName}/{subGroup}";
    }

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(name.Select(c => invalid.Contains(c) || c == '/' ? '_' : c)).Trim();
        return string.IsNullOrEmpty(sanitized) ? "Unknown" : sanitized;
    }
}

internal sealed record QueryFilesParams(
    string AnimationId,
    string? RelativeDir = null);

internal sealed record FileListResult(string AnimationTitle, string Path, List<FileSummary> Files);
internal sealed record FileSummary(string FileName, bool IsDirectory, string Path);
