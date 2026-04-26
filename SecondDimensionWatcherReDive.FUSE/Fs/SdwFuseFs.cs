using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SecondDimensionWatcherReDive.FUSE.Client;
using SecondDimensionWatcherReDive.FUSE.Native;

namespace SecondDimensionWatcherReDive.FUSE.Fs;

// Implements the FUSE filesystem callbacks. Each entry point is a static
// `[UnmanagedCallersOnly]` method that libfuse invokes from one of its worker
// threads — these threads have no managed context, so we look the singleton up
// from a static field, then drop into managed code immediately. Async client
// calls are awaited synchronously: libfuse already runs us on a pool of worker
// threads, so blocking one is fine and avoids the cost of building a custom
// awaiter for each request.
internal sealed unsafe partial class SdwFuseFs
{
    // FUSE worker threads have no managed sync context, so blocking the worker
    // is exactly what libfuse expects. Push the singleton into a static field so
    // the unmanaged trampolines can recover the instance without per-call state.
    private static SdwFuseFs? _instance;

    private readonly SdwClient _client;
    private readonly AttrCache _cache;
    private readonly FileHandleTable _handles = new();
    private readonly ILogger<SdwFuseFs> _logger;
    private readonly uint _uid;
    private readonly uint _gid;

    public SdwFuseFs(SdwClient client, AttrCache cache, ILogger<SdwFuseFs> logger)
    {
        _client = client;
        _cache = cache;
        _logger = logger;
        _uid = GetCurrentUserId();
        _gid = GetCurrentGroupId();
    }

    [LibraryImport("libc", EntryPoint = "geteuid")]
    private static partial uint GetCurrentUserId();

    [LibraryImport("libc", EntryPoint = "getegid")]
    private static partial uint GetCurrentGroupId();

    public static SdwFuseFs Current => _instance
        ?? throw new InvalidOperationException("SdwFuseFs has not been installed.");

    public void InstallAsCurrent()
    {
        if (Interlocked.CompareExchange(ref _instance, this, null) is not null)
            throw new InvalidOperationException("Another SdwFuseFs is already installed.");
    }

    public static FuseOperations BuildOperations() => new()
    {
        getattr = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, LinuxStat*, FuseFileInfo*, int>)&Getattr,
        readdir = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, void*, IntPtr, long, FuseFileInfo*, FuseReaddirFlags, int>)&Readdir,
        open = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, FuseFileInfo*, int>)&Open,
        read = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, byte*, nuint, long, FuseFileInfo*, int>)&Read,
        release = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, FuseFileInfo*, int>)&Release,
        access = (IntPtr)(delegate* unmanaged[Cdecl]<byte*, int, int>)&Access,
    };

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static int Getattr(byte* pathPtr, LinuxStat* statPtr, FuseFileInfo* fi)
    {
        try
        {
            var path = Marshal.PtrToStringUTF8((IntPtr)pathPtr) ?? "/";
            return Current.GetattrImpl(path, statPtr);
        }
        catch (Exception ex)
        {
            Current._logger.LogError(ex, "getattr failed");
            return -Errno.EIO;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static int Readdir(byte* pathPtr, void* buf, IntPtr filler, long offset,
        FuseFileInfo* fi, FuseReaddirFlags flags)
    {
        try
        {
            var path = Marshal.PtrToStringUTF8((IntPtr)pathPtr) ?? "/";
            return Current.ReaddirImpl(path, buf, filler);
        }
        catch (Exception ex)
        {
            Current._logger.LogError(ex, "readdir failed");
            return -Errno.EIO;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static int Open(byte* pathPtr, FuseFileInfo* fi)
    {
        try
        {
            var path = Marshal.PtrToStringUTF8((IntPtr)pathPtr) ?? "/";
            return Current.OpenImpl(path, fi);
        }
        catch (Exception ex)
        {
            Current._logger.LogError(ex, "open failed");
            return -Errno.EIO;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static int Read(byte* pathPtr, byte* buf, nuint size, long offset, FuseFileInfo* fi)
    {
        try
        {
            var path = Marshal.PtrToStringUTF8((IntPtr)pathPtr) ?? "/";
            return Current.ReadImpl(path, buf, (int)size, offset, fi);
        }
        catch (Exception ex)
        {
            Current._logger.LogError(ex, "read failed");
            return -Errno.EIO;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static int Release(byte* pathPtr, FuseFileInfo* fi)
    {
        try
        {
            Current._handles.Release(fi->fh);
            return 0;
        }
        catch (Exception ex)
        {
            Current._logger.LogError(ex, "release failed");
            return -Errno.EIO;
        }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static int Access(byte* pathPtr, int mask)
    {
        // Mount is read-only — refuse W_OK queries; everything else is allowed
        // because mode bits already say 0555/0444 and the kernel enforces them.
        if ((mask & AccessMode.W_OK) != 0) return -Errno.EROFS;
        return 0;
    }

    private int GetattrImpl(string path, LinuxStat* statPtr)
    {
        var entry = ResolveEntry(path);
        if (entry is null) return -Errno.ENOENT;

        FillStat(statPtr, entry);
        return 0;
    }

    private int ReaddirImpl(string path, void* buf, IntPtr fillerPtr)
    {
        var entries = ResolveList(path);
        if (entries is null) return -Errno.ENOENT;

        var filler = (delegate* unmanaged[Cdecl]<void*, byte*, LinuxStat*, long, FuseFillDirFlags, int>)fillerPtr;

        if (EmitName(buf, filler, ".") != 0) return 0;
        if (EmitName(buf, filler, "..") != 0) return 0;

        foreach (var child in entries)
        {
            LinuxStat stat;
            FillStat(&stat, child);
            if (EmitChild(buf, filler, child.Name, &stat) != 0) break;
        }
        return 0;
    }

    private int OpenImpl(string path, FuseFileInfo* fi)
    {
        if ((fi->flags & OpenFlags.O_ACCMODE) != OpenFlags.O_RDONLY) return -Errno.EROFS;

        var entry = ResolveEntry(path);
        if (entry is null) return -Errno.ENOENT;
        if (entry.IsDirectory) return -Errno.EISDIR;

        var handle = _handles.Allocate(path);
        fi->fh = handle;
        return 0;
    }

    private int ReadImpl(string path, byte* buf, int size, long offset, FuseFileInfo* fi)
    {
        if (size <= 0) return 0;
        // FUSE may set fi->fh to 0 if the kernel re-opened the file (unusual for
        // read-only mounts), so fall back to the path libfuse handed us.
        var virtualPath = _handles.TryGet(fi->fh, out var open) ? open.VirtualPath : path;

        // Pool a managed buffer; copying once is unavoidable because the destination
        // is unmanaged FUSE memory we don't own.
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(size);
        try
        {
            var read = _client.ReadAsync(virtualPath, offset, rented, 0, size, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (read < 0) return read;
            new ReadOnlySpan<byte>(rented, 0, read).CopyTo(new Span<byte>(buf, read));
            return read;
        }
        catch (SdwUnauthorizedException)
        {
            return -Errno.EACCES;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "read transport failure for {Path}", virtualPath);
            return -Errno.EIO;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private VfsEntry? ResolveEntry(string path)
    {
        if (_cache.TryGetStat(path, out var cached)) return cached;
        try
        {
            var entry = _client.StatAsync(path, CancellationToken.None).GetAwaiter().GetResult();
            if (entry is not null) _cache.PutStat(path, entry);
            return entry;
        }
        catch (SdwUnauthorizedException ex)
        {
            _logger.LogError(ex, "stat unauthorized");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "stat transport failure for {Path}", path);
            return null;
        }
    }

    private VfsEntry[]? ResolveList(string path)
    {
        if (_cache.TryGetList(path, out var cached)) return cached;
        try
        {
            var entries = _client.ListAsync(path, CancellationToken.None).GetAwaiter().GetResult();
            if (entries is not null) _cache.PutList(path, entries);
            return entries;
        }
        catch (SdwUnauthorizedException ex)
        {
            _logger.LogError(ex, "list unauthorized");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "list transport failure for {Path}", path);
            return null;
        }
    }

    private void FillStat(LinuxStat* statPtr, VfsEntry entry)
    {
        Unsafe.InitBlockUnaligned(statPtr, 0, (uint)sizeof(LinuxStat));
        if (entry.IsDirectory)
        {
            statPtr->st_mode = LinuxFileMode.DirectoryReadOnly;
            statPtr->st_nlink = 2;
            statPtr->st_size = 0;
        }
        else
        {
            statPtr->st_mode = LinuxFileMode.FileReadOnly;
            statPtr->st_nlink = 1;
            statPtr->st_size = entry.Size ?? 0;
            statPtr->st_blksize = 4096;
            statPtr->st_blocks = (statPtr->st_size + 511) / 512;
        }
        statPtr->st_uid = _uid;
        statPtr->st_gid = _gid;

        if (entry.LastModifiedUtc is { } mtime)
        {
            var sec = mtime.ToUnixTimeSeconds();
            statPtr->st_mtime_sec = sec;
            statPtr->st_atime_sec = sec;
            statPtr->st_ctime_sec = sec;
        }
    }

    private static int EmitName(void* buf,
        delegate* unmanaged[Cdecl]<void*, byte*, LinuxStat*, long, FuseFillDirFlags, int> filler,
        string name)
        => InvokeFiller(buf, filler, name, null, FuseFillDirFlags.None);

    private static int EmitChild(void* buf,
        delegate* unmanaged[Cdecl]<void*, byte*, LinuxStat*, long, FuseFillDirFlags, int> filler,
        string name, LinuxStat* stat)
        => InvokeFiller(buf, filler, name, stat, FuseFillDirFlags.Plus);

    private static int InvokeFiller(void* buf,
        delegate* unmanaged[Cdecl]<void*, byte*, LinuxStat*, long, FuseFillDirFlags, int> filler,
        string name, LinuxStat* stat, FuseFillDirFlags flags)
    {
        var maxBytes = System.Text.Encoding.UTF8.GetMaxByteCount(name.Length) + 1;
        Span<byte> buffer = maxBytes <= 256 ? stackalloc byte[256] : new byte[maxBytes];
        var written = System.Text.Encoding.UTF8.GetBytes(name, buffer);
        buffer[written] = 0;
        fixed (byte* p = buffer)
        {
            return filler(buf, p, stat, 0, flags);
        }
    }
}
