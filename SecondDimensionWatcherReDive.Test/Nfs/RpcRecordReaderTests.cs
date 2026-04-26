using System.Buffers.Binary;
using SecondDimensionWatcherReDive.NFS.Rpc;

namespace SecondDimensionWatcherReDive.Test.Nfs;

[TestClass]
public class RpcRecordReaderTests
{
    [TestMethod]
    public async Task ReadAsync_SingleFragment()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream();
        WriteFragment(stream, payload, isLast: true);
        stream.Position = 0;

        using var record = await RpcRecordReader.ReadAsync(stream, 1024, CancellationToken.None);
        Assert.IsNotNull(record);
        CollectionAssert.AreEqual(payload, record!.Span.ToArray());
    }

    [TestMethod]
    public async Task ReadAsync_MultipleFragmentsAreReassembled()
    {
        var part1 = new byte[] { 0xAA, 0xBB };
        var part2 = new byte[] { 0xCC, 0xDD, 0xEE };
        var stream = new MemoryStream();
        WriteFragment(stream, part1, isLast: false);
        WriteFragment(stream, part2, isLast: true);
        stream.Position = 0;

        using var record = await RpcRecordReader.ReadAsync(stream, 1024, CancellationToken.None);
        Assert.IsNotNull(record);
        CollectionAssert.AreEqual(
            new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0xEE }, record!.Span.ToArray());
    }

    [TestMethod]
    public async Task ReadAsync_EofReturnsNull()
    {
        var stream = new MemoryStream();
        var record = await RpcRecordReader.ReadAsync(stream, 1024, CancellationToken.None);
        Assert.IsNull(record);
    }

    [TestMethod]
    public async Task ReadAsync_OversizeRejected()
    {
        var payload = new byte[2048];
        var stream = new MemoryStream();
        WriteFragment(stream, payload, isLast: true);
        stream.Position = 0;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
        {
            using var _ = await RpcRecordReader.ReadAsync(stream, 1024, CancellationToken.None);
        });
    }

    [TestMethod]
    public async Task WriteAsync_SetsLastFragmentBitAndLength()
    {
        var payload = new byte[] { 0x10, 0x20, 0x30 };
        var stream = new MemoryStream();
        await RpcRecordReader.WriteAsync(stream, payload, CancellationToken.None);

        var bytes = stream.ToArray();
        Assert.AreEqual(7, bytes.Length);
        var header = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        Assert.AreEqual(RpcConstants.LastFragmentMask | (uint)payload.Length, header);
        CollectionAssert.AreEqual(payload, bytes[4..]);
    }

    private static void WriteFragment(Stream stream, byte[] payload, bool isLast)
    {
        var headerVal = (isLast ? RpcConstants.LastFragmentMask : 0) | (uint)payload.Length;
        var headerBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(headerBytes, headerVal);
        stream.Write(headerBytes);
        stream.Write(payload);
    }
}
