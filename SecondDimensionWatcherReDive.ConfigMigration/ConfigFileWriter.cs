using System.Runtime.InteropServices;
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
        try
        {
            await using (var stream = new FileStream(temporary, options))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(contents), cancellationToken);
                stream.Flush(flushToDisk: true);
                if (!OperatingSystem.IsWindows())
                {
                    if (OperatingSystem.IsLinux()) PreserveOwner(path, stream.SafeFileHandle);
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                }
            }
            await using (var stream = new FileStream(backup, options))
            {
                await stream.WriteAsync(original, cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!(await File.ReadAllBytesAsync(path, cancellationToken)).AsSpan().SequenceEqual(original))
                throw new ConfigMigrationException("Configuration changed during migration. No migration was written; retry with the new file.");
            // Same-directory atomic replacement. The version and every Up commit together.
            File.Move(temporary, path, overwrite: true);
            return backup;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
