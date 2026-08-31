using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Rpc;
using SecondDimensionWatcherReDive.NFS.Vfs;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.NFS.Server;

internal sealed partial class NfsCompoundDispatcher(
    NfsVfsAdapter vfs,
    NfsClientRegistry clients,
    NfsOpenStateRegistry opens,
    IOptions<NfsOptions> options,
    ILogger<NfsCompoundDispatcher> logger)
{
    private static readonly byte[] s_zeroVerifier = new byte[8];
    private const int MaxReadDirBytes = 4 * 1024 * 1024;

    public async Task<NfsCompoundResult> DispatchAsync(NfsCompoundRequest request, NfsRequestContext context)
    {
        if (request.MinorVersion != 0)
            return new NfsCompoundResult(NfsConstants.ErrMinorVersMismatch, []);

        var results = new List<NfsOpResult>(request.Operations.Count);
        var lastStatus = NfsConstants.Ok;

        foreach (var op in request.Operations)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            uint status;
            try
            {
                status = await ExecuteOpAsync(op, context, writer);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogOpFailed(logger, ex, op.OpCode);
                buffer = new ArrayBufferWriter<byte>();
                writer = new XdrWriter(buffer);
                writer.WriteUInt32(NfsConstants.ErrServerFault);
                status = NfsConstants.ErrServerFault;
            }

            results.Add(new NfsOpResult(op.OpCode, buffer.WrittenSpan.ToArray()));
            lastStatus = status;
            if (status != NfsConstants.Ok)
                break;
        }

        return new NfsCompoundResult(lastStatus, results);
    }

    private Task<uint> ExecuteOpAsync(NfsOperation op, NfsRequestContext ctx, XdrWriter writer) => op switch
    {
        PutRootFhOp _ => Task.FromResult(HandlePutRootFh(ctx, writer)),
        PutFhOp p => Task.FromResult(HandlePutFh(p, ctx, writer)),
        GetFhOp _ => Task.FromResult(HandleGetFh(ctx, writer)),
        SaveFhOp _ => Task.FromResult(HandleSaveFh(ctx, writer)),
        RestoreFhOp _ => Task.FromResult(HandleRestoreFh(ctx, writer)),
        LookupOp l => HandleLookupAsync(l, ctx, writer),
        LookupPOp _ => HandleLookupPAsync(ctx, writer),
        GetAttrOp g => HandleGetAttrAsync(g, ctx, writer),
        AccessOp a => HandleAccessAsync(a, ctx, writer),
        ReadDirOp r => HandleReadDirAsync(r, ctx, writer),
        ReadOp r => HandleReadAsync(r, ctx, writer),
        OpenOp o => HandleOpenAsync(o, ctx, writer),
        OpenConfirmOp o => Task.FromResult(HandleOpenConfirm(o, writer)),
        CloseOp c => Task.FromResult(HandleClose(c, writer)),
        SetClientIdOp s => Task.FromResult(HandleSetClientId(s, writer)),
        SetClientIdConfirmOp s => Task.FromResult(HandleSetClientIdConfirm(s, writer)),
        RenewOp r => Task.FromResult(HandleRenew(r, writer)),
        SecInfoOp _ => Task.FromResult(HandleSecInfo(writer)),
        DelegReturnOp _ => Task.FromResult(WriteOk(writer)),
        ReleaseLockOwnerOp _ => Task.FromResult(WriteOk(writer)),
        UnsupportedOp u => Task.FromResult(WriteStatus(writer, u.MappedStatus)),
        _ => Task.FromResult(WriteStatus(writer, NfsConstants.ErrNotSupp))
    };

    private static uint WriteOk(XdrWriter writer) => WriteStatus(writer, NfsConstants.Ok);

    private static uint WriteStatus(XdrWriter writer, uint status)
    {
        writer.WriteUInt32(status);
        return status;
    }

    private static uint HandlePutRootFh(NfsRequestContext ctx, XdrWriter writer)
    {
        ctx.CurrentFh = NfsFileHandle.Root;
        return WriteOk(writer);
    }

    private static uint HandlePutFh(PutFhOp op, NfsRequestContext ctx, XdrWriter writer)
    {
        try
        {
            ctx.CurrentFh = NfsFileHandle.FromBytes(op.Handle);
            return WriteOk(writer);
        }
        catch (XdrException)
        {
            return WriteStatus(writer, NfsConstants.ErrBadHandle);
        }
    }

    private static uint HandleGetFh(NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);
        writer.WriteUInt32(NfsConstants.Ok);
        writer.WriteOpaque(ctx.CurrentFh.ToBytes());
        return NfsConstants.Ok;
    }

    private static uint HandleSaveFh(NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);
        ctx.SavedFh = ctx.CurrentFh;
        return WriteOk(writer);
    }

    private static uint HandleRestoreFh(NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.SavedFh is null)
            return WriteStatus(writer, NfsConstants.ErrServerFault);
        ctx.CurrentFh = ctx.SavedFh;
        return WriteOk(writer);
    }

    private async Task<uint> HandleLookupAsync(LookupOp op, NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);
        var parent = await vfs.ResolveAsync(ctx.CurrentFh, ctx.CancellationToken);
        if (parent is null)
            return WriteStatus(writer, NfsConstants.ErrStale);
        if (parent.Kind == NfsHandleKind.File)
            return WriteStatus(writer, NfsConstants.ErrNotDir);
        if (string.IsNullOrEmpty(op.Name))
            return WriteStatus(writer, NfsConstants.ErrInval);
        if (op.Name.Length > NfsConstants.MaxName)
            return WriteStatus(writer, NfsConstants.ErrNameTooLong);
        if (op.Name.Contains('/') || op.Name == "." || op.Name == "..")
            return WriteStatus(writer, NfsConstants.ErrBadName);

        var resolved = await vfs.LookupAsync(ctx.CurrentFh, op.Name, ctx.CancellationToken);
        if (resolved is null)
            return WriteStatus(writer, NfsConstants.ErrNoEnt);

        ctx.CurrentFh = new NfsFileHandle(resolved.Kind, resolved.EntryId);
        return WriteOk(writer);
    }

    private async Task<uint> HandleLookupPAsync(NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);
        if (ctx.CurrentFh.Kind == NfsHandleKind.Root)
            return WriteStatus(writer, NfsConstants.ErrNoEnt);

        var parent = await vfs.LookupParentAsync(ctx.CurrentFh, ctx.CancellationToken);
        if (parent is null)
            return WriteStatus(writer, NfsConstants.ErrStale);
        ctx.CurrentFh = parent.Kind == NfsHandleKind.Root
            ? NfsFileHandle.Root
            : new NfsFileHandle(parent.Kind, parent.EntryId);
        return WriteOk(writer);
    }

    private async Task<uint> HandleGetAttrAsync(GetAttrOp op, NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);

        var resolved = await vfs.ResolveAsync(ctx.CurrentFh, ctx.CancellationToken);
        if (resolved is null)
            return WriteStatus(writer, NfsConstants.ErrStale);

        var attrs = BuildAttrSource(ctx, ctx.CurrentFh, resolved);
        writer.WriteUInt32(NfsConstants.Ok);
        NfsAttributes.EncodeGetAttrResponse(writer, op.AttrRequest, attrs);
        return NfsConstants.Ok;
    }

    private async Task<uint> HandleAccessAsync(AccessOp op, NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);

        var resolved = await vfs.ResolveAsync(ctx.CurrentFh, ctx.CancellationToken);
        if (resolved is null)
            return WriteStatus(writer, NfsConstants.ErrStale);

        var supported = resolved.Kind == NfsHandleKind.File
            ? NfsConstants.Access4Read
            : NfsConstants.Access4Read | NfsConstants.Access4Lookup;
        var access = op.Mask & supported;

        writer.WriteUInt32(NfsConstants.Ok);
        writer.WriteUInt32(supported);
        writer.WriteUInt32(access);
        return NfsConstants.Ok;
    }

    private async Task<uint> HandleReadDirAsync(ReadDirOp op, NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);
        if (ctx.CurrentFh.Kind == NfsHandleKind.File)
            return WriteStatus(writer, NfsConstants.ErrNotDir);

        if (op.Cookie > long.MaxValue)
            return WriteStatus(writer, NfsConstants.ErrBadCookie);
        var maxBytes = (long)Math.Min(op.MaxCount, MaxReadDirBytes);
        const int statusBytes = 4;
        const int verifierBytes = 8;
        const int trailerBytes = 4 + 4;
        if (maxBytes < statusBytes + verifierBytes + trailerBytes)
            return WriteStatus(writer, NfsConstants.ErrTooSmall);

        var page = await vfs.ListPageAsync(
            ctx.CurrentFh,
            op.Cookie == 0 ? null : (long)op.Cookie,
            512,
            ctx.CancellationToken);
        if (page is null)
            return WriteStatus(writer, NfsConstants.ErrStale);
        if (!page.CursorIsValid)
            return WriteStatus(writer, NfsConstants.ErrBadCookie);
        if (page.Generation <= 0)
            return WriteStatus(writer, NfsConstants.ErrServerFault);
        if (op.Cookie != 0 && op.Verifier != (ulong)page.Generation)
            return WriteStatus(writer, NfsConstants.ErrBadCookie);

        var body = new ArrayBufferWriter<byte>();
        var bodyWriter = new XdrWriter(body);
        bodyWriter.WriteUInt64((ulong)page.Generation);

        var emitted = 0;
        var directoryBytes = 0L;
        foreach (var child in page.Items)
        {
            var childHandle = new NfsFileHandle(child.Kind, child.EntryId);
            var attrs = BuildAttrSource(ctx, childHandle, child.Size, child.MTime);

            var directoryInfoBuffer = new ArrayBufferWriter<byte>();
            var directoryInfoWriter = new XdrWriter(directoryInfoBuffer);
            directoryInfoWriter.WriteUInt32(1);
            directoryInfoWriter.WriteUInt64((ulong)child.Cookie);
            directoryInfoWriter.WriteString(child.Name);

            var entryBuffer = new ArrayBufferWriter<byte>();
            var entryWriter = new XdrWriter(entryBuffer);
            entryWriter.WriteRaw(directoryInfoBuffer.WrittenSpan);
            NfsAttributes.EncodeGetAttrResponse(entryWriter, op.AttrRequest, attrs);

            var nextDirectoryBytes = directoryBytes
                                     + directoryInfoBuffer.WrittenCount
                                     + sizeof(uint);
            var nextTotalBytes = statusBytes
                                 + body.WrittenCount
                                 + entryBuffer.WrittenCount
                                 + trailerBytes;
            if (nextDirectoryBytes > op.DirCount || nextTotalBytes > maxBytes)
                break;

            bodyWriter.WriteRaw(entryBuffer.WrittenSpan);
            directoryBytes += directoryInfoBuffer.WrittenCount;
            emitted++;
        }

        if (emitted == 0 && page.Items.Count > 0)
            return WriteStatus(writer, NfsConstants.ErrTooSmall);

        bodyWriter.WriteUInt32(0);
        var eof = emitted == page.Items.Count && !page.HasMore;
        bodyWriter.WriteBool(eof);

        writer.WriteUInt32(NfsConstants.Ok);
        writer.WriteRaw(body.WrittenSpan);
        return NfsConstants.Ok;
    }

    private async Task<uint> HandleReadAsync(ReadOp op, NfsRequestContext ctx, XdrWriter writer)
    {
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);
        if (ctx.CurrentFh.Kind != NfsHandleKind.File)
            return WriteStatus(writer, NfsConstants.ErrIsDir);
        if (!opens.Validate(op.StateId))
            return WriteStatus(writer, NfsConstants.ErrBadStateId);

        var resolved = await vfs.ResolveAsync(ctx.CurrentFh, ctx.CancellationToken);
        if (resolved is null)
            return WriteStatus(writer, NfsConstants.ErrStale);
        if (op.Offset > long.MaxValue)
            return WriteStatus(writer, NfsConstants.ErrFBig);

        var count = (int)Math.Min(op.Count, NfsConstants.MaxRead);
        var buffer = ArrayPool<byte>.Shared.Rent(count);
        try
        {
            await using var stream = await vfs.OpenReadAsync(ctx.CurrentFh, ctx.CancellationToken);
            if (stream.CanSeek)
                stream.Seek((long)op.Offset, SeekOrigin.Begin);
            else
                await SkipAsync(stream, (long)op.Offset, ctx.CancellationToken);

            var totalRead = 0;
            var reachedEnd = false;
            while (totalRead < count)
            {
                var read = await stream.ReadAsync(
                    buffer.AsMemory(totalRead, count - totalRead), ctx.CancellationToken);
                if (read == 0)
                {
                    reachedEnd = true;
                    break;
                }
                totalRead += read;
            }

            var eof = reachedEnd;
            if (!eof && stream.CanSeek)
            {
                eof = stream.Position >= stream.Length;
            }
            else if (!eof && resolved.Size > 0)
            {
                eof = (long)op.Offset + totalRead >= resolved.Size;
            }
            writer.WriteUInt32(NfsConstants.Ok);
            writer.WriteBool(eof);
            writer.WriteOpaque(buffer.AsSpan(0, totalRead));
            return NfsConstants.Ok;
        }
        catch (IOException) when (!ctx.CancellationToken.IsCancellationRequested)
        {
            return WriteStatus(writer, NfsConstants.ErrIo);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task<uint> HandleOpenAsync(OpenOp op, NfsRequestContext ctx, XdrWriter writer)
    {
        if (op.OpenType != NfsConstants.Open4NoCreate)
            return WriteStatus(writer, NfsConstants.ErrRoFs);
        if ((op.ShareAccess & NfsConstants.Open4ShareAccessWrite) != 0)
            return WriteStatus(writer, NfsConstants.ErrOpenMode);
        if (op.ClaimType != 0)
            return WriteStatus(writer, NfsConstants.ErrNotSupp);
        if (ctx.CurrentFh is null)
            return WriteStatus(writer, NfsConstants.ErrNoFileHandle);
        if (ctx.CurrentFh.Kind == NfsHandleKind.File)
            return WriteStatus(writer, NfsConstants.ErrNotDir);

        var parent = await vfs.ResolveAsync(ctx.CurrentFh, ctx.CancellationToken);
        if (parent is null)
            return WriteStatus(writer, NfsConstants.ErrStale);
        if (parent.Kind == NfsHandleKind.File)
            return WriteStatus(writer, NfsConstants.ErrNotDir);
        var resolved = await vfs.LookupAsync(ctx.CurrentFh, op.FileName, ctx.CancellationToken);
        if (resolved is null)
            return WriteStatus(writer, NfsConstants.ErrNoEnt);
        if (resolved.Kind != NfsHandleKind.File)
            return WriteStatus(writer, NfsConstants.ErrIsDir);

        ctx.CurrentFh = new NfsFileHandle(NfsHandleKind.File, resolved.EntryId);
        var stateId = opens.Allocate(op.ClientId, ctx.CurrentFh.ToBytes());

        writer.WriteUInt32(NfsConstants.Ok);
        stateId.WriteTo(writer);
        // change_info4: atomic, before, after
        writer.WriteBool(true);
        writer.WriteUInt64(0);
        writer.WriteUInt64(0);
        // rflags: require OPEN_CONFIRM + signal POSIX-style locking
        writer.WriteUInt32(NfsConstants.Open4ResultConfirm | NfsConstants.Open4ResultLocktypePosix);
        // attrset bitmap: empty
        writer.WriteUInt32(0);
        // open_delegation4: OPEN_DELEGATE_NONE = 0
        writer.WriteUInt32(0);
        return NfsConstants.Ok;
    }

    private uint HandleOpenConfirm(OpenConfirmOp op, XdrWriter writer)
    {
        if (!opens.Validate(op.StateId))
            return WriteStatus(writer, NfsConstants.ErrBadStateId);
        var bumped = opens.Confirm(op.StateId);
        writer.WriteUInt32(NfsConstants.Ok);
        bumped.WriteTo(writer);
        return NfsConstants.Ok;
    }

    private uint HandleClose(CloseOp op, XdrWriter writer)
    {
        if (!opens.Validate(op.StateId))
            return WriteStatus(writer, NfsConstants.ErrBadStateId);
        opens.Close(op.StateId);
        writer.WriteUInt32(NfsConstants.Ok);
        var closedState = new NfsStateId(op.SeqId + 1, op.StateId.OtherHi, op.StateId.OtherLo);
        closedState.WriteTo(writer);
        return NfsConstants.Ok;
    }

    private uint HandleSetClientId(SetClientIdOp op, XdrWriter writer)
    {
        var clientId = clients.RegisterUnconfirmed(op.Verifier, op.ClientIdBytes);
        writer.WriteUInt32(NfsConstants.Ok);
        writer.WriteUInt64(clientId);
        // setclientid_confirm verifier4 (8 bytes); echo a constant
        writer.WriteFixedOpaque(s_zeroVerifier);
        return NfsConstants.Ok;
    }

    private uint HandleSetClientIdConfirm(SetClientIdConfirmOp op, XdrWriter writer)
    {
        if (!clients.Confirm(op.ClientId))
            return WriteStatus(writer, NfsConstants.ErrStaleClientId);
        return WriteOk(writer);
    }

    private uint HandleRenew(RenewOp op, XdrWriter writer)
    {
        if (!clients.Renew(op.ClientId))
            return WriteStatus(writer, NfsConstants.ErrStaleClientId);
        return WriteOk(writer);
    }

    private static uint HandleSecInfo(XdrWriter writer)
    {
        writer.WriteUInt32(NfsConstants.Ok);
        writer.WriteUInt32(1);
        writer.WriteUInt32(RpcConstants.AuthSys);
        return NfsConstants.Ok;
    }

    private AttrSource BuildAttrSource(NfsRequestContext ctx, NfsFileHandle handle, NfsResolvedNode resolved)
        => new(
            resolved.Kind == NfsHandleKind.Directory || resolved.Kind == NfsHandleKind.Root,
            resolved.Size,
            resolved.MTime,
            handle,
            $"{ctx.Credential.Uid}@sdw",
            $"{ctx.Credential.Gid}@sdw",
            options.Value.LeaseSeconds);

    private AttrSource BuildAttrSource(NfsRequestContext ctx, NfsFileHandle handle, long size, DateTimeOffset mtime)
        => new(
            handle.Kind == NfsHandleKind.Directory || handle.Kind == NfsHandleKind.Root,
            size,
            mtime,
            handle,
            $"{ctx.Credential.Uid}@sdw",
            $"{ctx.Credential.Gid}@sdw",
            options.Value.LeaseSeconds);

    private static async Task SkipAsync(Stream stream, long count, CancellationToken cancellationToken)
    {
        var buf = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (count > 0)
            {
                var toRead = (int)Math.Min(buf.Length, count);
                var read = await stream.ReadAsync(buf.AsMemory(0, toRead), cancellationToken);
                if (read == 0) break;
                count -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "NFS op {OpCode} failed")]
    private static partial void LogOpFailed(ILogger logger, Exception ex, uint opCode);
}
