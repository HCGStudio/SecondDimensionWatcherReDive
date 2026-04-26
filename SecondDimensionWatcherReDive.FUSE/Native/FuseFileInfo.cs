using System.Runtime.InteropServices;

namespace SecondDimensionWatcherReDive.FUSE.Native;

// Subset of libfuse3 `struct fuse_file_info`. We only touch `flags` (read by `open`)
// and `fh` (we set this; libfuse passes it back on subsequent calls). The rest of the
// struct is laid out to match libfuse3 so callers see the right offsets.
[StructLayout(LayoutKind.Sequential)]
internal struct FuseFileInfo
{
    public int flags;
    // bitfield word in libfuse3 (writepage/direct_io/keep_cache/flush/nonseekable/...)
    public uint bitfields;
    public uint padding;
    public ulong fh;
    public ulong lock_owner;
    public uint poll_events;
}

[Flags]
internal enum FuseReaddirFlags : uint
{
    None = 0,
    Plus = 1u << 0,
}

internal enum FuseFillDirFlags : uint
{
    None = 0,
    Plus = 1u << 1,
}
