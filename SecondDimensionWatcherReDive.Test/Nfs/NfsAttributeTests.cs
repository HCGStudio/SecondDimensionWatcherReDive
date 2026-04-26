using System.Buffers;
using SecondDimensionWatcherReDive.NFS;
using SecondDimensionWatcherReDive.NFS.Protocol;
using SecondDimensionWatcherReDive.NFS.Xdr;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class NfsAttributeTests
{
    [TestMethod]
    public void Bitmap_RoundTrip()
    {
        int[] ids =
        [
            NfsConstants.FattrType,
            NfsConstants.FattrSize,
            NfsConstants.FattrFilehandle,
            NfsConstants.FattrMode
        ];

        var bitmap = NfsAttributes.BitmapFromIds(ids);
        var decoded = NfsAttributes.IdsFromBitmap(bitmap);
        CollectionAssert.AreEquivalent(ids, decoded);
    }

    [TestMethod]
    public void EncodeGetAttr_DirSizeAndType()
    {
        var attrSource = new AttrSource(
            IsDirectory: true,
            Size: 0,
            MTime: new DateTimeOffset(2026, 4, 27, 0, 0, 0, TimeSpan.Zero),
            Handle: NfsFileHandle.Root,
            OwnerName: "0@sdw",
            GroupName: "0@sdw",
            LeaseTimeSeconds: 90);

        var requestBitmap = NfsAttributes.BitmapFromIds(
            [NfsConstants.FattrType, NfsConstants.FattrSize]);

        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        NfsAttributes.EncodeGetAttrResponse(w, requestBitmap, attrSource);

        var r = new XdrReader(buf.WrittenSpan);
        var responseBitmap = r.ReadUInt32Array();
        var attrlist = r.ReadOpaque().ToArray();

        var responseIds = NfsAttributes.IdsFromBitmap(responseBitmap);
        CollectionAssert.AreEquivalent(
            new[] { NfsConstants.FattrType, NfsConstants.FattrSize }, responseIds);

        var inner = new XdrReader(attrlist);
        Assert.AreEqual(NfsConstants.Nf4Dir, inner.ReadUInt32());
        Assert.AreEqual(0ul, inner.ReadUInt64());
    }

    [TestMethod]
    public void EncodeGetAttr_UnsupportedAttrOmittedFromBitmap()
    {
        var attrSource = new AttrSource(
            IsDirectory: false,
            Size: 1024,
            MTime: DateTimeOffset.UnixEpoch,
            Handle: new NfsFileHandle(NfsHandleKind.File, "/x"),
            OwnerName: "0@sdw",
            GroupName: "0@sdw",
            LeaseTimeSeconds: 90);

        // 999 is not in our supported set
        var requestBitmap = NfsAttributes.BitmapFromIds([NfsConstants.FattrSize, 999]);

        var buf = new ArrayBufferWriter<byte>();
        var w = new XdrWriter(buf);
        NfsAttributes.EncodeGetAttrResponse(w, requestBitmap, attrSource);

        var r = new XdrReader(buf.WrittenSpan);
        var responseBitmap = r.ReadUInt32Array();
        var responseIds = NfsAttributes.IdsFromBitmap(responseBitmap);
        CollectionAssert.DoesNotContain(responseIds, 999);
        CollectionAssert.Contains(responseIds, NfsConstants.FattrSize);
    }

    [TestMethod]
    public void EncodeGetAttr_FileTypeIsRegular()
    {
        var source = new AttrSource(
            IsDirectory: false,
            Size: 100,
            MTime: DateTimeOffset.UnixEpoch,
            Handle: new NfsFileHandle(NfsHandleKind.File, "/x"),
            OwnerName: "0@sdw",
            GroupName: "0@sdw",
            LeaseTimeSeconds: 90);

        var bitmap = NfsAttributes.BitmapFromIds([NfsConstants.FattrType]);
        var buf = new ArrayBufferWriter<byte>();
        NfsAttributes.EncodeGetAttrResponse(new XdrWriter(buf), bitmap, source);

        var r = new XdrReader(buf.WrittenSpan);
        _ = r.ReadUInt32Array();
        var inner = new XdrReader(r.ReadOpaque());
        Assert.AreEqual(NfsConstants.Nf4Reg, inner.ReadUInt32());
    }
}
