using System.Runtime.InteropServices;

namespace SecondDimensionWatcherReDive.FUSE.Native;

// Minimal P/Invoke surface against libfuse3 (system-installed `libfuse3.so.3`).
// We only need `fuse_main_real`; libfuse handles the kernel handshake, mount
// option parsing, signal handlers, and the request loop on our behalf.
internal static unsafe partial class LibFuse
{
    private const string LibraryName = "fuse3";

    [LibraryImport(LibraryName, EntryPoint = "fuse_main_real")]
    public static partial int fuse_main_real(
        int argc,
        byte** argv,
        FuseOperations* op,
        nuint op_size,
        void* user_data);
}
