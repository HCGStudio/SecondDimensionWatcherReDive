using System.Buffers.Binary;
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
                    var originalOwner = OperatingSystem.IsLinux() ? PreserveOwner(path, stream.SafeFileHandle) : null;
                    File.SetUnixFileMode(temporary, File.GetUnixFileMode(path));
                    if (OperatingSystem.IsLinux()) PreservePosixAccessAcl(path, stream.SafeFileHandle, originalOwner);
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
            var originalOwner = OperatingSystem.IsLinux() ? PreserveOwner(path, migrationLock.SafeFileHandle) : null;
            // The inode carries no secrets and is never executable. Its owner can
            // always reacquire it; other accounts still need configuration write access.
            var readWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
            File.SetUnixFileMode(lockPath, (File.GetUnixFileMode(path) & readWrite) |
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            if (OperatingSystem.IsLinux()) PreservePosixAccessAcl(path, migrationLock.SafeFileHandle, originalOwner, readWriteOnly: true);
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

    private static FileStat? PreserveOwner(string path, SafeFileHandle destination)
    {
        if (Statx(-100, path, 0, 0x1a, out var stat) != 0)
            throw new IOException("Cannot read configuration ownership.");
        if (Fchown(destination, stat.UserId, stat.GroupId) == 0) return null;
        if (Marshal.GetLastPInvokeError() != 1) // EPERM: an authorized writer cannot transfer its new inode's UID.
            throw new IOException("Cannot preserve configuration ownership.");
        // Keep the original group when the writer belongs to it. Otherwise the
        // ACL below retains that group's effective access as a named entry.
        if (Fchown(destination, uint.MaxValue, stat.GroupId) != 0 && Marshal.GetLastPInvokeError() != 1)
            throw new IOException("Cannot preserve configuration group ownership.");
        return stat;
    }

    private const string PosixAccessAcl = "system.posix_acl_access";

    private static void PreservePosixAccessAcl(string path, SafeFileHandle destination,
        FileStat? originalOwner = null, bool readWriteOnly = false)
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
                if (originalOwner is { } ownership)
                {
                    PreserveNonOwnerAccess(destination, ownership, [], readWriteOnly);
                    return;
                }
                RemoveInheritedPosixAcl(destination);
                return;
            }
            throw new IOException("Cannot read configuration POSIX access control.");
        }
        if (originalOwner is { } original)
        {
            PreserveNonOwnerAccess(destination, original, acl.AsSpan(0, (int)length), readWriteOnly);
            return;
        }
        if (readWriteOnly)
            for (var offset = 6; offset < (int)length; offset += 8)
                BinaryPrimitives.WriteUInt16LittleEndian(acl.AsSpan(offset),
                    BinaryPrimitives.ReadUInt16LittleEndian(acl.AsSpan(offset - 2)) == 1 ? (ushort)6
                        : (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(acl.AsSpan(offset)) & 6));
        if (FsetXattr(destination, PosixAccessAcl, acl, (nuint)length, 0) != 0)
            throw new IOException("Cannot preserve configuration POSIX access control.");
    }

    private readonly record struct AclEntry(ushort Tag, ushort Permissions, uint Id = uint.MaxValue);

    private static void PreserveNonOwnerAccess(SafeFileHandle destination,
        FileStat original, ReadOnlySpan<byte> acl, bool readWriteOnly)
    {
        if (Statx(destination.DangerousGetHandle().ToInt32(), "", 0x1000, 0x18, out var target) != 0)
            throw new IOException("Cannot read temporary configuration ownership.");
        var entries = new List<AclEntry>();
        if (acl.IsEmpty)
        {
            var mode = (int)original.Mode;
            entries.AddRange([new(1, (ushort)((mode >> 6) & 7)), new(4, (ushort)((mode >> 3) & 7)), new(32, (ushort)(mode & 7))]);
        }
        else
        {
            if (acl.Length < 4 || (acl.Length - 4) % 8 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(acl) != 2)
                throw new IOException("Cannot interpret configuration POSIX access control.");
            for (var offset = 4; offset < acl.Length; offset += 8)
                entries.Add(new(BinaryPrimitives.ReadUInt16LittleEndian(acl[offset..]),
                    BinaryPrimitives.ReadUInt16LittleEndian(acl[(offset + 2)..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(acl[(offset + 4)..])));
        }
        var owner = entries.Single(entry => entry.Tag == 1).Permissions;
        var group = entries.Single(entry => entry.Tag == 4).Permissions;
        var other = entries.Single(entry => entry.Tag == 32).Permissions;
        var mask = entries.FirstOrDefault(entry => entry.Tag == 16, new AclEntry(16, 7)).Permissions;
        var groups = ReadProcessGroups();
        var matchingGroups = entries.Where(entry => entry.Tag == 4 && groups.Contains(original.GroupId)
                                                   || entry.Tag == 8 && groups.Contains(entry.Id)).ToArray();
        var namedUser = entries.FirstOrDefault(entry => entry.Tag == 2 && entry.Id == target.UserId);
        var matchingPermissions = matchingGroups.Select(entry => (ushort)(entry.Permissions & mask)).ToArray();
        var groupWriter = matchingPermissions.FirstOrDefault(
            permission => matchingPermissions.All(otherPermission => (permission & otherPermission) == otherPermission),
            ushort.MaxValue);
        if (target.UserId != original.UserId && namedUser.Tag != 2 && matchingGroups.Length > 0
            && groupWriter == ushort.MaxValue)
            throw new IOException("Cannot retain separate group access rules after transferring configuration ownership.");
        var writer = target.UserId == original.UserId ? owner
            : namedUser.Tag == 2 ? (ushort)(namedUser.Permissions & mask)
            : matchingGroups.Length > 0 ? groupWriter
            : other;

        // Materialize effective permissions before changing the mask, so adding
        // the previous owner cannot broaden another user's or group's access.
        var users = entries.Where(entry => entry.Tag == 2 && entry.Id != target.UserId && entry.Id != original.UserId)
            .ToDictionary(entry => entry.Id, entry => (ushort)(entry.Permissions & mask));
        if (target.UserId != original.UserId) users[original.UserId] = owner;
        var namedGroups = entries.Where(entry => entry.Tag == 8)
            .ToDictionary(entry => entry.Id, entry => (ushort)(entry.Permissions & mask));
        var effectiveGroup = (ushort)(group & mask);
        if (namedGroups.TryGetValue(original.GroupId, out var duplicateGroup)
            && (duplicateGroup & effectiveGroup) != duplicateGroup && (duplicateGroup & effectiveGroup) != effectiveGroup)
            throw new IOException("Cannot retain separate access rules for the original configuration group.");
        namedGroups[original.GroupId] = (ushort)(namedGroups.GetValueOrDefault(original.GroupId) | effectiveGroup);
        if (!namedGroups.TryGetValue(target.GroupId, out var owningGroup))
        {
            // A new owning group must retain the access it previously obtained
            // through OTHER, without overriding a more restrictive old group rule.
            if (namedGroups.Values.Any(permission => (permission & other) != other))
                throw new IOException("Cannot retain configuration group access without preserving its original group.");
            owningGroup = other;
        }
        namedGroups.Remove(target.GroupId);
        var result = new List<AclEntry> { new(1, writer) };
        result.AddRange(users.OrderBy(pair => pair.Key).Select(pair => new AclEntry(2, pair.Value, pair.Key)));
        result.Add(new(4, owningGroup));
        result.AddRange(namedGroups.OrderBy(pair => pair.Key).Select(pair => new AclEntry(8, pair.Value, pair.Key)));
        if (readWriteOnly)
            for (var index = 0; index < result.Count; index++)
                if (result[index].Tag == 1 || result[index].Tag == 2 && result[index].Id == original.UserId)
                    result[index] = result[index] with { Permissions = 6 };
        result.Add(new(16, (ushort)result.Where(entry => entry.Tag is 2 or 4 or 8)
            .Aggregate(0, (permissions, entry) => permissions | entry.Permissions)));
        result.Add(new(32, other));
        var encoded = new byte[4 + result.Count * 8];
        BinaryPrimitives.WriteUInt32LittleEndian(encoded, 2);
        for (var index = 0; index < result.Count; index++)
        {
            var offset = 4 + index * 8;
            BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(offset), result[index].Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(offset + 2),
                (ushort)(result[index].Permissions & (readWriteOnly ? 6 : 7)));
            BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(offset + 4), result[index].Id);
        }
        if (FsetXattr(destination, PosixAccessAcl, encoded, (nuint)encoded.Length, 0) != 0)
            throw new IOException("Cannot retain configuration access without transferring ownership.");
    }

    private static HashSet<uint> ReadProcessGroups()
    {
        var count = GetGroups(0, null);
        if (count < 0) throw new IOException("Cannot read migration account groups.");
        var groups = new uint[count];
        if (GetGroups(groups.Length, groups) != count)
            throw new IOException("Cannot read migration account groups.");
        return groups.Append(GetEffectiveGroupId()).ToHashSet();
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
        [FieldOffset(28)] public ushort Mode;
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

    [DllImport("libc", EntryPoint = "getgroups", SetLastError = true)]
    private static extern int GetGroups(int size, [Out] uint[]? groups);

    [DllImport("libc", EntryPoint = "getegid")]
    private static extern uint GetEffectiveGroupId();
}
