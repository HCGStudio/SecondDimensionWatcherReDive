using System.Security.Cryptography;
using System.Text;

namespace SecondDimensionWatcherReDive.Auth;

internal interface IDeviceTokenHasher
{
    string Hash(string plaintext);

    bool Verify(string plaintext, string encodedHash);

    bool IsModernHash(string encodedHash);

    string VerificationCacheKey(Guid tokenId, string plaintext);
}

internal sealed class DeviceTokenHasher(string pepper) : IDeviceTokenHasher
{
    private const string Prefix = "$hmac-sha256$v1$";
    private readonly byte[] _pepper = GetPepper(pepper);

    public string Hash(string plaintext) => Prefix + Compute(plaintext);

    public bool Verify(string plaintext, string encodedHash)
    {
        if (!IsModernHash(encodedHash))
            return false;
        var expected = Encoding.ASCII.GetBytes(Hash(plaintext));
        var actual = Encoding.ASCII.GetBytes(encodedHash);
        return expected.Length == actual.Length &&
               CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public bool IsModernHash(string encodedHash) =>
        encodedHash.StartsWith(Prefix, StringComparison.Ordinal);

    public string VerificationCacheKey(Guid tokenId, string plaintext) =>
        $"device-token:{tokenId:N}:{Compute(plaintext)}";

    private string Compute(string plaintext)
    {
        var digest = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToBase64String(digest).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] GetPepper(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("The device-token pepper cannot be empty.", nameof(pepper));
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length < 32 ||
            value.StartsWith("<Please fill", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The device-token pepper must be at least 32 bytes.", nameof(pepper));
        return bytes;
    }
}
