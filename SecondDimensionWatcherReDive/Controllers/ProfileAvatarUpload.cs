using System.Buffers.Binary;

namespace SecondDimensionWatcherReDive.Controllers;

internal static class ProfileAvatarUpload
{
    internal const int MaxBytes = 2 * 1024 * 1024;

    // Identify raster content from its bytes; never trust a filename or supplied MIME type.
    // No SVG/HTML is accepted, and dimensions are bounded before browser decoding.
    internal static string? GetContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length is < 24 or > MaxBytes) return null;
        if (data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            if (data.Length < 45 || !data.Slice(12, 4).SequenceEqual("IHDR"u8)
                || BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4)) != 13
                || !data.Slice(data.Length - 12, 12).SequenceEqual(new byte[] { 0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130 })) return null;
            return ValidSize(BinaryPrimitives.ReadUInt32BigEndian(data.Slice(16, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(data.Slice(20, 4))) ? "image/png" : null;
        }
        if (data[0] != 0xff || data[1] != 0xd8 || data[^2] != 0xff || data[^1] != 0xd9) return null;
        for (var offset = 2; offset + 4 <= data.Length;)
        {
            if (data[offset++] != 0xff) return null;
            while (offset < data.Length && data[offset] == 0xff) offset++;
            if (offset + 3 > data.Length) return null;
            var marker = data[offset++];
            if (marker is 0xda or 0xd9) return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
            if (length < 2 || offset + length > data.Length) return null;
            if (marker is 0xc0 or 0xc1 or 0xc2)
            {
                if (length < 8) return null;
                return ValidSize(BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2))) ? "image/jpeg" : null;
            }
            offset += length;
        }
        return null;
    }

    private static bool ValidSize(uint width, uint height) =>
        width is > 0 and <= 4096 && height is > 0 and <= 4096;
}
