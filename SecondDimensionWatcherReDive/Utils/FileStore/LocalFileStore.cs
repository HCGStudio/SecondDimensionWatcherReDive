using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.Framework.FileStore;
using SecondDimensionWatcherReDive.Services;

namespace SecondDimensionWatcherReDive.Utils.FileStore;

public class LocalFileStore(IOptionsMonitor<MediaLibraryOptions> options) : IFileStore
{
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxOpenDirectory = 0x10000;
    private const int LinuxOpenNoFollow = 0x20000;
    private const int LinuxOpenCloseOnExec = 0x80000;

    public string Name => FileStores.LocalDiskStore;

    public Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var safePath = ResolveSafePath(path);
        if (OperatingSystem.IsLinux())
        {
            var handle = OpenLinuxPathWithoutFollowingLinks(safePath);
            try
            {
                return Task.FromResult<Stream>(new FileStream(handle, FileAccess.Read));
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        var stream = File.OpenRead(safePath);
        try
        {
            // Windows validates the final path attached to the opened handle, closing
            // the resolve/open race. Other non-Linux platforms retain a conservative
            // second path validation fallback.
            var openedPath = OperatingSystem.IsWindows()
                ? GetWindowsFinalPath(stream.SafeFileHandle)
                : OperatingSystem.IsMacOS()
                    ? GetMacOsFinalPath(stream.SafeFileHandle)
                : safePath;
            var verifiedPath = ResolveSafePath(openedPath);
            if (!MediaLibraryPath.PathEquals(safePath, verifiedPath))
                throw new UnauthorizedAccessException(
                    "The local file path changed while it was being opened.");
            return Task.FromResult<Stream>(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public async IAsyncEnumerable<FileStoreInfo> EnumerateDirectory(string path)
    {
        var safePath = ResolveSafePath(path);
        var fileInfo = new FileInfo(safePath);
        if (fileInfo.Exists)
        {
            yield return new FileStoreInfo(
                false,
                safePath,
                fileInfo.Name,
                fileInfo.Length,
                new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero));
            yield break;
        }

        var directoryInfo = new DirectoryInfo(safePath);
        if (!directoryInfo.Exists) yield break;

        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var entry in directoryInfo.EnumerateFileSystemInfos("*", enumerationOptions))
        {
            var safeEntryPath = ResolveSafePath(entry.FullName);
            var attributes = File.GetAttributes(safeEntryPath);
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            FileSystemInfo safeEntry = isDirectory
                ? new DirectoryInfo(safeEntryPath)
                : new FileInfo(safeEntryPath);
            long? length = !isDirectory && safeEntry is FileInfo safeFile
                ? safeFile.Length
                : null;
            yield return new FileStoreInfo(
                isDirectory,
                safeEntryPath,
                safeEntry.Name,
                length,
                new DateTimeOffset(safeEntry.LastWriteTimeUtc, TimeSpan.Zero));
        }

        await Task.CompletedTask;
    }

    public Task<FileStoreInfo> FileInfoAsync(
        string path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var safePath = ResolveSafePath(path);
        var fileAttributes = File.GetAttributes(safePath);
        var isDirectory = (fileAttributes & FileAttributes.Directory) != 0;
        FileSystemInfo fileSystemInfo = isDirectory
            ? new DirectoryInfo(safePath)
            : new FileInfo(safePath);
        long? length = !isDirectory && fileSystemInfo is FileInfo fileInfo
            ? fileInfo.Length
            : null;
        return Task.FromResult(new FileStoreInfo(
            isDirectory,
            fileSystemInfo.FullName,
            fileSystemInfo.Name,
            length,
            new DateTimeOffset(fileSystemInfo.LastWriteTimeUtc, TimeSpan.Zero)));
    }

    public Task<IReadOnlyDictionary<string, FileStoreInfo>> GetFileInfosAsync(
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, FileStoreInfo>(StringComparer.Ordinal);
        foreach (var path in paths.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var safePath = ResolveSafePath(path);
                var fileAttributes = File.GetAttributes(safePath);
                var isDirectory = (fileAttributes & FileAttributes.Directory) != 0;
                FileSystemInfo fileSystemInfo = isDirectory
                    ? new DirectoryInfo(safePath)
                    : new FileInfo(safePath);
                long? length = !isDirectory && fileSystemInfo is FileInfo fileInfo
                    ? fileInfo.Length
                    : null;
                results[path] = new FileStoreInfo(
                    isDirectory,
                    fileSystemInfo.FullName,
                    fileSystemInfo.Name,
                    length,
                    new DateTimeOffset(fileSystemInfo.LastWriteTimeUtc, TimeSpan.Zero));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Preserve the rest of the batch when one physical file is stale.
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, FileStoreInfo>>(results);
    }

    public Task<bool> ExistAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var safePath = ResolveSafePath(path);
            return Task.FromResult(File.Exists(safePath) || Directory.Exists(safePath));
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or NotSupportedException
                                          or UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    private string ResolveSafePath(string path)
    {
        var currentOptions = options.CurrentValue;
        var isManagedDownload = !string.IsNullOrWhiteSpace(currentOptions.DownloadRoot)
                                && MediaLibraryPath.IsLexicallyAllowed(
                                    path,
                                    [currentOptions.DownloadRoot]);
        var isMediaLibraryPath = MediaLibraryPath.IsLexicallyAllowed(
            path,
            currentOptions.AllowedRoots);
        if (!isManagedDownload && !isMediaLibraryPath)
            throw new UnauthorizedAccessException(
                $"Local file path '{path}' is outside the configured storage roots.");

        var resolvedPath = MediaLibraryPath.ResolveExistingPath(path);
        var remainsInExpectedRoot = isManagedDownload
            ? MediaLibraryPath.IsAllowed(
                resolvedPath,
                [currentOptions.DownloadRoot!])
            : MediaLibraryPath.IsAllowed(
                  resolvedPath,
                  currentOptions.AllowedRoots)
              && (string.IsNullOrWhiteSpace(currentOptions.DownloadRoot)
                  || !MediaLibraryPath.IsAllowed(
                      resolvedPath,
                      [currentOptions.DownloadRoot]));
        if (!remainsInExpectedRoot)
            throw new UnauthorizedAccessException(
                $"Local file path '{path}' resolves outside its configured storage root.");
        return resolvedPath;
    }

    private static SafeFileHandle OpenLinuxPathWithoutFollowingLinks(string path)
    {
        var normalized = MediaLibraryPath.Normalize(path);
        var root = Path.GetPathRoot(normalized)
                   ?? throw new ArgumentException("The file path has no root.", nameof(path));
        var segments = Path.GetRelativePath(root, normalized)
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new UnauthorizedAccessException("A directory root cannot be opened as a media file.");

        var rootDescriptor = LinuxOpen(
            root,
            LinuxOpenReadOnly
            | LinuxOpenDirectory
            | LinuxOpenNoFollow
            | LinuxOpenCloseOnExec);
        if (rootDescriptor < 0) ThrowLinuxOpenError(path);

        var current = new SafeFileHandle((nint)rootDescriptor, ownsHandle: true);
        try
        {
            for (var index = 0; index < segments.Length; index++)
            {
                var isLast = index == segments.Length - 1;
                var flags = LinuxOpenReadOnly
                            | LinuxOpenNoFollow
                            | LinuxOpenCloseOnExec;
                if (!isLast) flags |= LinuxOpenDirectory;

                var descriptor = LinuxOpenAt(
                    current.DangerousGetHandle().ToInt32(),
                    segments[index],
                    flags);
                if (descriptor < 0) ThrowLinuxOpenError(path);

                var next = new SafeFileHandle((nint)descriptor, ownsHandle: true);
                current.Dispose();
                current = next;
            }

            var result = current;
            current = new SafeFileHandle(nint.Zero, ownsHandle: false);
            return result;
        }
        finally
        {
            current.Dispose();
        }
    }

    private static void ThrowLinuxOpenError(string path)
    {
        var error = Marshal.GetLastPInvokeError();
        throw new UnauthorizedAccessException(
            $"Local file path '{path}' could not be opened without following links (errno {error}).");
    }

    [DllImport(
        "libc",
        EntryPoint = "open",
        SetLastError = true,
        CharSet = CharSet.Ansi,
        ExactSpelling = true)]
    private static extern int LinuxOpen(string path, int flags);

    [DllImport(
        "libc",
        EntryPoint = "openat",
        SetLastError = true,
        CharSet = CharSet.Ansi,
        ExactSpelling = true)]
    private static extern int LinuxOpenAt(int directoryDescriptor, string path, int flags);

    private static string GetWindowsFinalPath(SafeFileHandle handle)
    {
        const int maxPath = 32768;
        var buffer = new StringBuilder(maxPath);
        var length = GetFinalPathNameByHandle(handle, buffer, maxPath, 0);
        if (length == 0 || length >= maxPath)
            throw new UnauthorizedAccessException(
                $"Could not validate the opened local file handle (error {Marshal.GetLastPInvokeError()}).");

        var path = buffer.ToString();
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[uncPrefix.Length..];
        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[devicePrefix.Length..]
            : path;
    }

    [DllImport(
        "kernel32.dll",
        EntryPoint = "GetFinalPathNameByHandleW",
        SetLastError = true,
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder path,
        int pathLength,
        uint flags);

    private static string GetMacOsFinalPath(SafeFileHandle handle)
    {
        const int fGetPath = 50;
        const int maxPath = 1024;
        var buffer = new byte[maxPath];
        if (MacOsFcntl(
                handle.DangerousGetHandle().ToInt32(),
                fGetPath,
                buffer) != 0)
            throw new UnauthorizedAccessException(
                $"Could not validate the opened local file handle (errno {Marshal.GetLastPInvokeError()}).");

        var terminator = Array.IndexOf(buffer, (byte)0);
        if (terminator < 0) terminator = buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, terminator);
    }

    [DllImport(
        "libc",
        EntryPoint = "fcntl",
        SetLastError = true,
        ExactSpelling = true)]
    private static extern int MacOsFcntl(int fileDescriptor, int command, byte[] buffer);
}
