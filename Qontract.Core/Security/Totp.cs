using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Qontract.Core.Security;

/// <summary>RFC 6238: SHA-1, six digits, 30-second time steps.</summary>
public static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public static string NewSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var result = new StringBuilder(32);
        var buffer = 0; var bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; result.Append(Alphabet[(buffer >> bits) & 31]); }
        }
        return result.ToString();
    }

    public static string Code(string secret, long step)
    {
        var key = new List<byte>(); var buffer = 0; var bits = 0;
        foreach (var c in secret)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0) throw new FormatException("Invalid TOTP key.");
            buffer = (buffer << 5) | value; bits += 5;
            if (bits >= 8) { bits -= 8; key.Add((byte)(buffer >> bits)); }
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(key.ToArray(), counter);
        var offset = hash[^1] & 15;
        var value32 = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & int.MaxValue;
        return (value32 % 1000000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static long? Verify(string secret, string? code, long lastStep, DateTimeOffset now)
    {
        if (code is null || code.Length != 6 || code.Any(c => c < '0' || c > '9')) return null;
        var current = now.ToUnixTimeSeconds() / 30;
        for (var step = current + 1; step >= current - 1; step--)
            if (step > lastStep && CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(code))) return step;
        return null;
    }
}

public interface ITwoFactorSecretProtector
{
    string Protect(string secret);
    string Unprotect(string secret);
}
