using System.Runtime.InteropServices;

namespace SecondDimensionWatcherReDive.FUSE.Native;

// glibc's x86_64 stat ABI (144 bytes). ARM64 has a different layout below;
// sizeof(long) alone does not determine the native structure's ABI.
[StructLayout(LayoutKind.Sequential)]
internal struct LinuxStat
{
    public ulong st_dev;
    public ulong st_ino;
    public ulong st_nlink;
    public uint st_mode;
    public uint st_uid;
    public uint st_gid;
    public int __pad0;
    public ulong st_rdev;
    public long st_size;
    public long st_blksize;
    public long st_blocks;
    public long st_atime_sec;
    public long st_atime_nsec;
    public long st_mtime_sec;
    public long st_mtime_nsec;
    public long st_ctime_sec;
    public long st_ctime_nsec;
    public long __unused0;
    public long __unused1;
    public long __unused2;
}

// glibc's AArch64 stat ABI (128 bytes), including its 32-bit nlink/blksize
// fields and the padding before st_size. libfuse allocates this exact size.
[StructLayout(LayoutKind.Sequential)]
internal struct LinuxArm64Stat
{
    public ulong st_dev;
    public ulong st_ino;
    public uint st_mode;
    public uint st_nlink;
    public uint st_uid;
    public uint st_gid;
    public ulong st_rdev;
    public ulong __pad1;
    public long st_size;
    public int st_blksize;
    public int __pad2;
    public long st_blocks;
    public long st_atime_sec;
    public long st_atime_nsec;
    public long st_mtime_sec;
    public long st_mtime_nsec;
    public long st_ctime_sec;
    public long st_ctime_nsec;
    public uint __unused0;
    public uint __unused1;
}

internal static class LinuxFileMode
{
    public const uint S_IFMT = 0xF000;
    public const uint S_IFREG = 0x8000;
    public const uint S_IFDIR = 0x4000;
    public const uint S_IFLNK = 0xA000;

    public const uint DirectoryReadOnly = S_IFDIR | 0b101_101_101; // 0555
    public const uint FileReadOnly = S_IFREG | 0b100_100_100;      // 0444
}

internal static class OpenFlags
{
    public const int O_ACCMODE = 0x0003;
    public const int O_RDONLY = 0x0000;
    public const int O_WRONLY = 0x0001;
    public const int O_RDWR = 0x0002;
}

internal static class AccessMode
{
    public const int F_OK = 0;
    public const int X_OK = 1;
    public const int W_OK = 2;
    public const int R_OK = 4;
}
