using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SecondDimensionWatcherReDive.ConfigMigration;

internal static class MacConfigFileAccess
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";
    private const ushort PermissionBits = 0x0fff;
    private const ushort ReadWriteBits = 0x01b6; // 0666
    private const ushort OwnerReadWrite = 0x0180; // 0600

    internal sealed record Snapshot(uint Owner, uint Group, ushort Mode, byte[] Acl);

    internal static Snapshot Preserve(string sourcePath, SafeFileHandle destination, bool readWriteOnly = false)
    {
        using var source = File.OpenHandle(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var attributes = ReadAttributes(source);
        using var acl = ReadAcl(source);
        var mode = (ushort)(attributes.Mode & PermissionBits);
        if (readWriteOnly)
        {
            mode = (ushort)((mode & ReadWriteBits) | OwnerReadWrite);
            RemoveExecuteAllows(acl);
        }

        // Darwin copyfile can ignore ownership errors and merge directory-inherited
        // ACLs. Apply the exact source policy instead, checking every native result.
        if (Fchown(destination, attributes.Owner, attributes.Group) != 0
            || Fchmod(destination, mode) != 0
            || AclSetFd(destination, acl) != 0)
            throw new IOException("Cannot preserve macOS configuration ownership and access control.");

        var snapshot = new Snapshot(attributes.Owner, attributes.Group, mode, SerializeAcl(acl));
        Verify(destination, snapshot);
        return snapshot;
    }

    internal static void ProtectBackup(SafeFileHandle destination)
    {
        using var acl = CreateEmptyAcl();
        if (AclSetFd(destination, acl) != 0 || Fchmod(destination, OwnerReadWrite) != 0)
            throw new IOException("Cannot restrict macOS configuration backup access.");
        var attributes = ReadAttributes(destination);
        Verify(destination, new(attributes.Owner, attributes.Group, OwnerReadWrite, SerializeAcl(acl)));
    }

    internal static void Verify(SafeFileHandle destination, Snapshot expected)
    {
        var actual = ReadAttributes(destination);
        using var acl = ReadAcl(destination);
        if (actual.Owner != expected.Owner || actual.Group != expected.Group
            || (actual.Mode & PermissionBits) != expected.Mode
            || !SerializeAcl(acl).AsSpan().SequenceEqual(expected.Acl))
            throw new IOException("macOS did not retain the required configuration access policy.");
    }

    private static SecurityAttributes ReadAttributes(SafeFileHandle descriptor)
    {
        // sys/attr.h defines a fixed 5-bitmap request. These three attributes are
        // packed as uint32 length, uid_t, gid_t, and uint32 access mode on Darwin.
        var request = new AttributeList { BitmapCount = 5, CommonAttributes = 0x00038000 };
        if (Fgetattrlist(descriptor, ref request, out var result, 16, 0) != 0 || result.Length != 16)
            throw new IOException("Cannot read macOS configuration ownership and mode.");
        return result;
    }

    private static AclHandle ReadAcl(SafeFileHandle descriptor)
    {
        var acl = AclGetFd(descriptor);
        if (!acl.IsInvalid) return acl;
        var error = Marshal.GetLastPInvokeError();
        acl.Dispose();
        // Darwin reports ENOENT when the file has no extended ACL.
        if (error == 2) return CreateEmptyAcl();
        throw new IOException("Cannot read macOS configuration access control.");
    }

    private static AclHandle CreateEmptyAcl()
    {
        var acl = AclInit(0);
        if (!acl.IsInvalid) return acl;
        acl.Dispose();
        throw new IOException("Cannot allocate macOS configuration access control.");
    }

    private static byte[] SerializeAcl(AclHandle acl)
    {
        var size = AclSize(acl);
        if (size <= 0 || size > int.MaxValue)
            throw new IOException("Cannot read macOS configuration access control size.");
        var bytes = new byte[(int)size];
        if (AclCopyExt(bytes, acl, size) != size)
            throw new IOException("Cannot compare macOS configuration access control.");
        return bytes;
    }

    private static void RemoveExecuteAllows(AclHandle acl)
    {
        var position = 0; // ACL_FIRST_ENTRY; ACL_NEXT_ENTRY is -1 on Darwin.
        while (AclGetEntry(acl, position, out var entry) == 0)
        {
            position = -1;
            if (AclGetTagType(entry, out var tag) != 0)
                throw new IOException("Cannot read macOS migration lock access control.");
            if (tag != 1) continue; // Keep deny entries intact; only remove execute grants.
            if (AclGetPermissions(entry, out var permissions) != 0
                || AclSetPermissions(entry, permissions & ~(1ul << 3)) != 0)
                throw new IOException("Cannot restrict macOS migration lock execution.");
        }
        if (Marshal.GetLastPInvokeError() != 22) // EINVAL marks the end of Darwin's ACL iterator.
            throw new IOException("Cannot enumerate macOS migration lock access control.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeList
    {
        public ushort BitmapCount;
        public ushort Reserved;
        public uint CommonAttributes;
        public uint VolumeAttributes;
        public uint DirectoryAttributes;
        public uint FileAttributes;
        public uint ForkAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public uint Owner;
        public uint Group;
        public uint Mode;
    }

    private sealed class AclHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
    {
        protected override bool ReleaseHandle() => AclFree(handle) == 0;
    }

    [DllImport(LibSystem, EntryPoint = "fgetattrlist", SetLastError = true)]
    private static extern int Fgetattrlist(SafeFileHandle descriptor, ref AttributeList request,
        out SecurityAttributes result, nuint size, uint options);

    [DllImport(LibSystem, EntryPoint = "fchown", SetLastError = true)]
    private static extern int Fchown(SafeFileHandle descriptor, uint owner, uint group);

    [DllImport(LibSystem, EntryPoint = "fchmod", SetLastError = true)]
    private static extern int Fchmod(SafeFileHandle descriptor, ushort mode);

    [DllImport(LibSystem, EntryPoint = "acl_get_fd", SetLastError = true)]
    private static extern AclHandle AclGetFd(SafeFileHandle descriptor);

    [DllImport(LibSystem, EntryPoint = "acl_set_fd", SetLastError = true)]
    private static extern int AclSetFd(SafeFileHandle descriptor, AclHandle acl);

    [DllImport(LibSystem, EntryPoint = "acl_init", SetLastError = true)]
    private static extern AclHandle AclInit(int count);

    [DllImport(LibSystem, EntryPoint = "acl_free")]
    private static extern int AclFree(nint acl);

    [DllImport(LibSystem, EntryPoint = "acl_size", SetLastError = true)]
    private static extern nint AclSize(AclHandle acl);

    [DllImport(LibSystem, EntryPoint = "acl_copy_ext", SetLastError = true)]
    private static extern nint AclCopyExt([Out] byte[] buffer, AclHandle acl, nint size);

    [DllImport(LibSystem, EntryPoint = "acl_get_entry", SetLastError = true)]
    private static extern int AclGetEntry(AclHandle acl, int position, out nint entry);

    [DllImport(LibSystem, EntryPoint = "acl_get_tag_type", SetLastError = true)]
    private static extern int AclGetTagType(nint entry, out int tag);

    [DllImport(LibSystem, EntryPoint = "acl_get_permset_mask_np", SetLastError = true)]
    private static extern int AclGetPermissions(nint entry, out ulong permissions);

    [DllImport(LibSystem, EntryPoint = "acl_set_permset_mask_np", SetLastError = true)]
    private static extern int AclSetPermissions(nint entry, ulong permissions);
}
