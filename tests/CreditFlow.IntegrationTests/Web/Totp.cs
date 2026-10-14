using System.Globalization;
using System.Security.Cryptography;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>RFC 6238 TOTP (SHA-1, 30-second steps, 6 digits), as used by authenticator apps and ASP.NET Core Identity.</summary>
public static class Totp
{
    public static string Compute(string base32Key, DateTimeOffset? at = null)
    {
        var key = Base32Decode(base32Key.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant());
        var step = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30;

        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            counter[i] = (byte)(step & 0xFF);
            step >>= 8;
        }

#pragma warning disable CA5350 // RFC 6238 authenticator codes are defined with HMAC-SHA1 (as in ASP.NET Core Identity).
        var hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
