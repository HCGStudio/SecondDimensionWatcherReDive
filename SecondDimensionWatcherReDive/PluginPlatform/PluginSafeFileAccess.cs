using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SecondDimensionWatcherReDive.PluginPlatform;

internal sealed record PluginFileEntry(
    string Name,
    bool IsDirectory,
    long? Length,
    DateTimeOffset LastModifiedUtc);

/// <summary>
/// Opens every POSIX path component relative to an already-open directory with O_NOFOLLOW.
/// This pins the object being accessed and closes rename/symlink races after lexical approval.
/// Windows reads validate the final path attached to the opened handle.
/// </summary>
internal sealed class PluginSafeFileAccess
{
    private const int ReadOnly = 0;
    private const int WriteOnly = 1;
    private const int LinuxCreate = 0x40;
    private const int LinuxExclusive = 0x80;
    private const int LinuxDirectory = 0x10000;
    private const int LinuxNoFollow = 0x20000;
    private const int LinuxCloseOnExec = 0x80000;
    private const int LinuxNonBlocking = 0x800;
    private const int MacCreate = 0x200;
    private const int MacExclusive = 0x800;
    private const int MacDirectory = 0x100000;
    private const int MacNoFollow = 0x100;
    private const int MacCloseOnExec = 0x1000000;
    private const int MacNonBlocking = 0x4;
    private const int MissingPathError = 2;

    internal Action? BeforeOpenForTesting { get; set; }

    public async Task<byte[]> ReadAsync(
        string root,
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ValidateLexicalPath(root, path);
        BeforeOpenForTesting?.Invoke();
        await using var stream = OpenRead(root, path);
        using var memory = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (memory.Length + read > maximumBytes)
                throw new InvalidDataException("Capability response exceeds the configured size limit.");
            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return memory.ToArray();
    }

    public IReadOnlyList<PluginFileEntry> List(string root, string path, int maximumEntries)
    {
        ValidateLexicalPath(root, path);
        BeforeOpenForTesting?.Invoke();
        if (IsPosix)
        {
            using var directory = OpenPosixAbsolute(path, directory: true, out _);
            var descriptorPath = GetDescriptorPath(directory);
            var result = new List<PluginFileEntry>();
            foreach (var item in Directory.EnumerateFileSystemEntries(descriptorPath))
            {
                if (result.Count >= maximumEntries)
                    throw new InvalidDataException("Directory contains too many entries.");
                var name = Path.GetFileName(item);
                SafeFileHandle child;
                try
                {
                    child = OpenPosixAt(directory, name, directory: false, out _);
                }
                catch (UnauthorizedAccessException exception) when (exception.InnerException is FileNotFoundException)
                {
                    continue;
                }
                using (child)
                {
                    var childPath = GetDescriptorPath(child);
                    var isDirectory = Directory.Exists(childPath);
                    result.Add(new PluginFileEntry(
                        name,
                        isDirectory,
                        isDirectory ? null : RandomAccess.GetLength(child),
                        new DateTimeOffset(File.GetLastWriteTimeUtc(childPath), TimeSpan.Zero)));
                }
            }
            return result;
        }

        throw new PlatformNotSupportedException(
            "Plugin directory capabilities are disabled on Windows until handle-relative enumeration is available.");
    }

    public PluginFileEntry? Info(string root, string path)
    {
        ValidateLexicalPath(root, path);
        BeforeOpenForTesting?.Invoke();
        if (IsPosix)
        {
            SafeFileHandle handle;
            try
            {
                handle = OpenPosixAbsolute(path, directory: false, out _);
            }
            catch (UnauthorizedAccessException exception) when (exception.InnerException is FileNotFoundException)
            {
                return null;
            }
            using (handle)
            {
                var descriptorPath = GetDescriptorPath(handle);
                var isDirectory = Directory.Exists(descriptorPath);
                return new PluginFileEntry(
                    Path.GetFileName(path),
                    isDirectory,
                    isDirectory ? null : RandomAccess.GetLength(handle),
                    new DateTimeOffset(File.GetLastWriteTimeUtc(descriptorPath), TimeSpan.Zero));
            }
        }

        throw new PlatformNotSupportedException(
            "Plugin metadata capabilities are disabled on Windows until handle-relative inspection is available.");
    }

    public bool Exists(string root, string path) => Info(root, path) is not null;

    public async Task WriteAsync(
        string root,
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        ValidateLexicalPath(root, path);
        BeforeOpenForTesting?.Invoke();
        if (IsPosix)
        {
            await WritePosixAsync(root, path, content, cancellationToken);
            return;
        }

        throw new PlatformNotSupportedException(
            "Plugin data writes are disabled on Windows until handle-relative creation is available.");
    }

    private static Stream OpenRead(string root, string path)
    {
        if (IsPosix)
        {
            var handle = OpenPosixAbsolute(path, directory: false, out _);
            try { return new FileStream(handle, FileAccess.Read); }
            catch { handle.Dispose(); throw; }
        }

        var windowsHandle = OpenWindowsPath(root, path, directory: false);
        try { return new FileStream(windowsHandle, FileAccess.Read); }
        catch { windowsHandle.Dispose(); throw; }
    }

    private static async Task WritePosixAsync(
        string root,
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        using var rootHandle = OpenOrCreatePosixDirectoryAbsolute(root);
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) throw new UnauthorizedAccessException("A plugin data root cannot be overwritten.");

        SafeFileHandle current = DuplicateHandleReference(rootHandle);
        try
        {
            foreach (var segment in segments[..^1])
            {
                SafeFileHandle next;
                try
                {
                    next = OpenPosixAt(current, segment, directory: true, out _);
                }
                catch (UnauthorizedAccessException exception) when (exception.InnerException is FileNotFoundException)
                {
                    if (PosixMkdirAt(current.DangerousGetHandle().ToInt32(), segment, Convert.ToUInt32("700", 8)) != 0 &&
                        Marshal.GetLastPInvokeError() != 17)
                        ThrowPosixError(Path.Combine(root, relative));
                    next = OpenPosixAt(current, segment, directory: true, out _);
                }
                current.Dispose();
                current = next;
            }

            var temporaryName = $".sdw-{Guid.NewGuid():N}.tmp";
            var descriptor = PosixOpenAt(
                current.DangerousGetHandle().ToInt32(),
                temporaryName,
                WriteOnly | CreateFlag | ExclusiveFlag | NoFollowFlag | CloseOnExecFlag,
                Convert.ToUInt32("600", 8));
            if (descriptor < 0) ThrowPosixError(path);
            try
            {
                await using (var stream = new FileStream(
                                 new SafeFileHandle((nint)descriptor, ownsHandle: false), FileAccess.Write))
                {
                    await stream.WriteAsync(content, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                if (PosixRenameAt(
                        current.DangerousGetHandle().ToInt32(), temporaryName,
                        current.DangerousGetHandle().ToInt32(), segments[^1]) != 0)
                    ThrowPosixError(path);
            }
            finally
            {
                _ = PosixUnlinkAt(current.DangerousGetHandle().ToInt32(), temporaryName, 0);
                new SafeFileHandle((nint)descriptor, ownsHandle: true).Dispose();
            }
        }
        finally
        {
            current.Dispose();
        }
    }

    private static SafeFileHandle DuplicateHandleReference(SafeFileHandle handle)
    {
        var duplicate = PosixOpenAt(handle.DangerousGetHandle().ToInt32(), ".",
            ReadOnly | DirectoryFlag | NoFollowFlag | CloseOnExecFlag, 0);
        if (duplicate < 0) ThrowPosixError(".");
        return new SafeFileHandle((nint)duplicate, ownsHandle: true);
    }

    private static SafeFileHandle OpenPosixAbsolute(string path, bool directory, out int error)
    {
        var normalized = Path.GetFullPath(path);
        var root = Path.GetPathRoot(normalized)!;
        var descriptor = PosixOpen(root, ReadOnly | DirectoryFlag | NoFollowFlag | CloseOnExecFlag, 0);
        if (descriptor < 0) ThrowPosixError(path);
        var current = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        try
        {
            var segments = Path.GetRelativePath(root, normalized)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < segments.Length; index++)
            {
                var next = OpenPosixAt(current, segments[index], directory && index == segments.Length - 1,
                    out error);
                current.Dispose();
                current = next;
            }
            error = 0;
            var result = current;
            current = new SafeFileHandle(nint.Zero, ownsHandle: false);
            return result;
        }
        finally
        {
            current.Dispose();
        }
    }

    private static SafeFileHandle OpenOrCreatePosixDirectoryAbsolute(string path)
    {
        var normalized = Path.GetFullPath(path);
        var root = Path.GetPathRoot(normalized)!;
        var descriptor = PosixOpen(root, ReadOnly | DirectoryFlag | NoFollowFlag | CloseOnExecFlag, 0);
        if (descriptor < 0) ThrowPosixError(path);
        var current = new SafeFileHandle((nint)descriptor, ownsHandle: true);
        try
        {
            foreach (var segment in Path.GetRelativePath(root, normalized)
                         .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                SafeFileHandle next;
                try
                {
                    next = OpenPosixAt(current, segment, directory: true, out _);
                }
                catch (UnauthorizedAccessException exception) when (exception.InnerException is FileNotFoundException)
                {
                    if (PosixMkdirAt(current.DangerousGetHandle().ToInt32(), segment, Convert.ToUInt32("700", 8)) != 0 &&
                        Marshal.GetLastPInvokeError() != 17)
                        ThrowPosixError(path);
                    next = OpenPosixAt(current, segment, directory: true, out _);
                }
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

    private static SafeFileHandle OpenPosixAt(
        SafeFileHandle parent,
        string name,
        bool directory,
        out int error)
    {
        var flags = ReadOnly | NoFollowFlag | CloseOnExecFlag | NonBlockingFlag;
        if (directory) flags |= DirectoryFlag;
        var descriptor = PosixOpenAt(parent.DangerousGetHandle().ToInt32(), name, flags, 0);
        if (descriptor < 0)
        {
            error = Marshal.GetLastPInvokeError();
            ThrowPosixError(name, error);
        }
        error = 0;
        return new SafeFileHandle((nint)descriptor, ownsHandle: true);
    }

    private static void ValidateLexicalPath(string root, string path)
    {
        root = Path.GetFullPath(root);
        path = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathFullyQualified(relative))
            throw new UnauthorizedAccessException("Plugin file path escapes its approved root.");
    }

    private static SafeFileHandle OpenWindowsPath(string root, string path, bool directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        const uint genericRead = 0x80000000;
        const uint shareAll = 0x00000007;
        const uint openExisting = 3;
        const uint backupSemantics = 0x02000000;
        const uint openReparsePoint = 0x00200000;
        var handle = WindowsCreateFile(path, genericRead, shareAll, 0, openExisting,
            backupSemantics | openReparsePoint, 0);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new UnauthorizedAccessException($"Plugin file path '{path}' could not be opened safely.");
        }
        try
        {
            RejectWindowsReparsePoint(path);
            var finalPath = GetWindowsFinalPath(handle);
            ValidateLexicalPath(root, finalPath);
            if (!Path.GetFullPath(path).Equals(Path.GetFullPath(finalPath), StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Plugin file path changed while it was being opened.");
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void RejectWindowsReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Reparse points are not allowed in plugin file paths.");
    }

    private static string GetWindowsFinalPath(SafeFileHandle handle)
    {
        const int maximumPath = 32768;
        var buffer = new StringBuilder(maximumPath);
        var length = WindowsGetFinalPathNameByHandle(handle, buffer, maximumPath, 0);
        if (length == 0 || length >= maximumPath)
            throw new UnauthorizedAccessException("Could not validate the opened plugin file handle.");
        var path = buffer.ToString();
        const string uncPrefix = @"\\?\UNC\";
        const string devicePrefix = @"\\?\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[uncPrefix.Length..];
        return path.StartsWith(devicePrefix, StringComparison.OrdinalIgnoreCase)
            ? path[devicePrefix.Length..]
            : path;
    }

    private static string GetDescriptorPath(SafeFileHandle handle)
        => OperatingSystem.IsLinux()
            ? $"/proc/self/fd/{handle.DangerousGetHandle().ToInt32()}"
            : $"/dev/fd/{handle.DangerousGetHandle().ToInt32()}";

    private static void ThrowPosixError(string path, int? knownError = null)
    {
        var error = knownError ?? Marshal.GetLastPInvokeError();
        Exception? inner = error == MissingPathError ? new FileNotFoundException(path) : null;
        throw new UnauthorizedAccessException(
            $"Plugin file path '{path}' could not be opened without following links (errno {error}).", inner);
    }

    private static bool IsPosix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    private static int DirectoryFlag => OperatingSystem.IsMacOS() ? MacDirectory : LinuxDirectory;
    private static int NoFollowFlag => OperatingSystem.IsMacOS() ? MacNoFollow : LinuxNoFollow;
    private static int CloseOnExecFlag => OperatingSystem.IsMacOS() ? MacCloseOnExec : LinuxCloseOnExec;
    private static int CreateFlag => OperatingSystem.IsMacOS() ? MacCreate : LinuxCreate;
    private static int ExclusiveFlag => OperatingSystem.IsMacOS() ? MacExclusive : LinuxExclusive;
    private static int NonBlockingFlag => OperatingSystem.IsMacOS() ? MacNonBlocking : LinuxNonBlocking;

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int PosixOpen(string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int PosixOpenAt(int directoryDescriptor, string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int PosixMkdirAt(int directoryDescriptor, string path, uint mode);

    [DllImport("libc", EntryPoint = "renameat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int PosixRenameAt(int oldDirectoryDescriptor, string oldPath,
        int newDirectoryDescriptor, string newPath);

    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int PosixUnlinkAt(int directoryDescriptor, string path, int flags);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle WindowsCreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true,
        CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint WindowsGetFinalPathNameByHandle(
        SafeFileHandle file, StringBuilder path, int pathLength, uint flags);
}
