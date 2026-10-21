using System.Security.Cryptography;

namespace PayslipArchiver.Domain.Archiving;

/// <summary>
/// Content hashing for duplicate detection. Payroll systems occasionally re-send the same slip
/// (or the same slip arrives via two routes); the decrypted bytes are what we compare.
/// </summary>
public static class ContentFingerprint
{
    public static string Sha256Hex(ReadOnlySpan<byte> content)
        => Convert.ToHexStringLower(SHA256.HashData(content));
}