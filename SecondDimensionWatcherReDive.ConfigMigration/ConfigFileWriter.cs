using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SecondDimensionWatcherReDive.ConfigMigration;

internal static class ConfigFileWriter
{
    internal static async Task<string> ReplaceAsync(
        string path, byte[] original, string contents, CancellationToken cancellationToken)
    {
        var suffix = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        var temporary = $"{path}.{suffix}.tmp";
        var backup = $"{path}.{suffix}.bak";
        FileSecurity? windowsSecurity = null;
        if (OperatingSystem.IsWindows())
        {
            windowsSecurity = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
            windowsSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        }
        using var migrationLock = OpenMigrationLock(path, windowsSecurity);
        if (!(await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(original))
            throw new ConfigMigrationException("Configuration changed during migration. No migration was written; retry with the new file.");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        try
        {
            await using (var stream = CreateOutput(temporary, options, windowsSecurity))
            {
                if (!OperatingSystem.IsWindows())
                {
                    if (OperatingSystem.IsLinux()) PreserveOwner(path, stream.SafeFileHandle);
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                    if (OperatingSystem.IsLinux()) PreservePosixAccessAcl(path, stream.SafeFileHandle);
                }
                // Establish the final access policy before writing any configuration data.
                await stream.WriteAsync(Encoding.UTF8.GetBytes(contents), cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            await using (var stream = CreateOutput(backup, options, windowsSecurity))
            {
                if (OperatingSystem.IsLinux()) RemoveInheritedPosixAcl(stream.SafeFileHandle);
                await stream.WriteAsync(original, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!(await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(original))
                throw new ConfigMigrationException("Configuration changed during migration. No migration was written; retry with the new file.");
            // Same-directory atomic replacement. The version and every Up commit together.
            if (OperatingSystem.IsWindows())
                File.Replace(temporary, path, destinationBackupFileName: null, ignoreMetadataErrors: false);
            else
                File.Move(temporary, path, overwrite: true);
            return backup;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static FileStream OpenMigrationLock(string path, FileSecurity? windowsSecurity)
    {
        // Keep one stable inode for cooperating writers, but do not leave an
        // administrator-only sidecar beside a configuration owned by the service.
        var lockPath = path + ".migration.lock";
        if (OperatingSystem.IsWindows())
            return new FileInfo(lockPath).Create(FileMode.OpenOrCreate,
                FileSystemRights.Read | FileSystemRights.Write, FileShare.None, 4096,
                FileOptions.None, windowsSecurity ?? throw new IOException("Cannot preserve migration lock access control."));

        FileStream migrationLock;
        try
        {
            migrationLock = new FileStream(lockPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });
        }
        catch (IOException) when (File.Exists(lockPath))
        {
            // Existing compatible locks need no chmod/chown privilege. In
            // particular, an ACL-authorized account may use an inode it does not own.
            return new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        try
        {
            if (OperatingSystem.IsLinux())
            {
                PreserveOwner(path, migrationLock.SafeFileHandle);
                PreservePosixAccessAcl(path, migrationLock.SafeFileHandle);
            }
            // The inode carries no secrets and is never executable. Its owner can
            // always reacquire it; other accounts still need configuration write access.
            var readWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
            File.SetUnixFileMode(lockPath, (File.GetUnixFileMode(path) & readWrite) |
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return migrationLock;
        }
        catch
        {
            migrationLock.Dispose();
            throw;
        }
    }

    private static FileStream CreateOutput(string path, FileStreamOptions options, FileSecurity? windowsSecurity)
    {
        if (OperatingSystem.IsWindows())
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write,
                FileShare.None, options.BufferSize, options.Options,
                windowsSecurity ?? throw new IOException("Cannot preserve configuration access control."));
        return new FileStream(path, options);
    }

    private static void PreserveOwner(string path, SafeFileHandle destination)
    {
        if (Statx(-100, path, 0, 0x18, out var stat) != 0)
            throw new IOException("Cannot read configuration ownership.");
        if (Fchown(destination, stat.UserId, stat.GroupId) != 0)
            throw new IOException("Cannot preserve configuration ownership.");
    }

    private const string PosixAccessAcl = "system.posix_acl_access";

    private static void PreservePosixAccessAcl(string path, SafeFileHandle destination)
    {
        // Linux limits an xattr value to 64 KiB. Read it in one operation so a
        // size probe cannot race an ACL change and yield a truncated policy.
        var acl = new byte[64 * 1024];
        var length = GetXattr(path, PosixAccessAcl, acl, (nuint)acl.Length);
        if (length < 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is 61 or 95) // ENODATA / EOPNOTSUPP: no POSIX access ACL.
            {
                RemoveInheritedPosixAcl(destination);
                return;
            }
            throw new IOException("Cannot read configuration POSIX access control.");
        }
        if (FsetXattr(destination, PosixAccessAcl, acl, (nuint)length, 0) != 0)
            throw new IOException("Cannot preserve configuration POSIX access control.");
    }

    private static void RemoveInheritedPosixAcl(SafeFileHandle destination)
    {
        // A temporary file may inherit named entries from its directory. They
        // must not become effective when the original file has only mode bits.
        if (FremoveXattr(destination, PosixAccessAcl) != 0 &&
            Marshal.GetLastPInvokeError() is not (61 or 95))
            throw new IOException("Cannot clear inherited configuration POSIX access control.");
    }

    // Linux statx has one stable layout on x64 and arm64 (unlike struct stat).
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct FileStat
    {
        [FieldOffset(20)] public uint UserId;
        [FieldOffset(24)] public uint GroupId;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out FileStat stat);

    [DllImport("libc", EntryPoint = "getxattr", SetLastError = true)]
    private static extern nint GetXattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [Out] byte[] value, nuint size);

    [DllImport("libc", EntryPoint = "fsetxattr", SetLastError = true)]
    private static extern int FsetXattr(SafeFileHandle descriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[] value, nuint size, int flags);

    [DllImport("libc", EntryPoint = "fremovexattr", SetLastError = true)]
    private static extern int FremoveXattr(SafeFileHandle descriptor,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("libc", EntryPoint = "fchown", SetLastError = true)]
    private static extern int Fchown(SafeFileHandle descriptor, uint owner, uint group);
}
