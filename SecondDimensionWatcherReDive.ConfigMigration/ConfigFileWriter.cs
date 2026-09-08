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
        // Cooperating CLI/startup writers lock the same persistent sidecar inode. Do not
        // unlink this file on release: another process may already be waiting on it.
        var lockOptions = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows()) lockOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var migrationLock = new FileStream(path + ".migration.lock", lockOptions);
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
        FileSecurity? windowsSecurity = null;
        if (OperatingSystem.IsWindows())
        {
            windowsSecurity = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
            // Preserve the effective DACL at creation time, before either file contains
            // secrets. Do not inherit broader permissions from the containing directory.
            windowsSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        }
        try
        {
            await using (var stream = CreateOutput(temporary, options, windowsSecurity))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(contents), cancellationToken);
                stream.Flush(flushToDisk: true);
                if (!OperatingSystem.IsWindows())
                {
                    if (OperatingSystem.IsLinux()) PreserveOwner(path, stream.SafeFileHandle);
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                }
            }
            await using (var stream = CreateOutput(backup, options, windowsSecurity))
            {
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

    [DllImport("libc", EntryPoint = "fchown", SetLastError = true)]
    private static extern int Fchown(SafeFileHandle descriptor, uint owner, uint group);
}
