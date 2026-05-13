using System.Security.Cryptography;

namespace ZitiDesktopEdge.UITests.MockIpc;

/// <summary>
/// RFC 6238 TOTP (HMAC-SHA1, 30s period, 6 digits) plus RFC 4648 base32 codec.
/// Used by MockIpcServer to issue real otpauth secrets on EnableMFA and to
/// validate codes submitted via VerifyMFA against the current time window.
/// Tests can call MockIpcServer.GetMfaSecret + this class's Compute to generate
/// the same code the mock will accept.
/// </summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int PeriodSeconds = 30;
    private const int Digits = 6;

    /// <summary>Generate a random base32-encoded secret of the requested byte length.</summary>
    public static string GenerateSecret(int bytes = 20)
    {
        var buf = new byte[bytes];
        RandomNumberGenerator.Fill(buf);
        return Base32Encode(buf);
    }

    /// <summary>
    /// Compute the TOTP code for the given base32 secret at the given UTC time
    /// (defaults to now). Returns a zero-padded 6-digit string.
    /// </summary>
    public static string Compute(string base32Secret, DateTimeOffset? utc = null)
    {
        var t = utc ?? DateTimeOffset.UtcNow;
        long counter = t.ToUnixTimeSeconds() / PeriodSeconds;
        var counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--) { counterBytes[i] = (byte)(counter & 0xFF); counter >>= 8; }
        var key = Base32Decode(base32Secret);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);
        int offset = hash[hash.Length - 1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);
        int otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString(new string('0', Digits));
    }

    /// <summary>
    /// Validate a submitted code against the current and adjacent time windows
    /// (±1 = ±30s of clock skew tolerance). Mirrors how RFC 6238 implementations
    /// typically validate.
    /// </summary>
    public static bool Validate(string base32Secret, string submittedCode, int allowedSkewWindows = 1)
    {
        var now = DateTimeOffset.UtcNow;
        for (int delta = -allowedSkewWindows; delta <= allowedSkewWindows; delta++)
        {
            var t = now.AddSeconds(delta * PeriodSeconds);
            if (Compute(base32Secret, t) == submittedCode) return true;
        }
        return false;
    }

    public static string Base32Encode(byte[] data)
    {
        var sb = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Base32Alphabet[(buffer >> bits) & 0x1F]);
            }
        }
        if (bits > 0) sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);
        return sb.ToString();
    }

    public static byte[] Base32Decode(string s)
    {
        s = s.TrimEnd('=').ToUpperInvariant();
        var bytes = new List<byte>((s.Length * 5 + 7) / 8);
        int buffer = 0, bits = 0;
        foreach (var c in s)
        {
            int v = Base32Alphabet.IndexOf(c);
            if (v < 0) continue;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        return bytes.ToArray();
    }
}
