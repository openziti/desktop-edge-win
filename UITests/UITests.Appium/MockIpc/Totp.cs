using System.Security.Cryptography;

namespace ZitiDesktopEdge.UITests.MockIpc;

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 30s period, 6 digits) with an RFC 4648 base32 codec.</summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int PeriodSeconds = 30;
    private const int Digits = 6;
    private const int SecretBytes = 20;
    // One period either side tolerates 30s of clock skew.
    private const int AllowedSkewWindows = 1;

    /// <summary>Base32 text of 20 random bytes.</summary>
    public static string GenerateSecret()
    {
        byte[] buf = new byte[SecretBytes];
        RandomNumberGenerator.Fill(buf);
        return Base32Encode(buf);
    }

    /// <summary>Zero-padded 6-digit code for the secret at <paramref name="utc"/>.</summary>
    public static string Compute(string base32Secret, DateTimeOffset utc)
    {
        long counter = utc.ToUnixTimeSeconds() / PeriodSeconds;
        byte[] counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--) { counterBytes[i] = (byte)(counter & 0xFF); counter >>= 8; }
        byte[] key = Base32Decode(base32Secret);
        using HMACSHA1 hmac = new HMACSHA1(key);
        byte[] hash = hmac.ComputeHash(counterBytes);
        int offset = hash[hash.Length - 1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);
        int otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString(new string('0', Digits));
    }

    /// <summary>True when the code matches the current window or one period either side of it.</summary>
    public static bool Validate(string base32Secret, string submittedCode)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int delta = -AllowedSkewWindows; delta <= AllowedSkewWindows; delta++)
        {
            DateTimeOffset t = now.AddSeconds(delta * PeriodSeconds);
            if (Compute(base32Secret, t) == submittedCode) return true;
        }
        return false;
    }

    public static string Base32Encode(byte[] data)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte b in data)
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
        string text = s.TrimEnd('=').ToUpperInvariant();
        List<byte> bytes = new List<byte>((text.Length * 5 + 7) / 8);
        int buffer = 0, bits = 0;
        foreach (char c in text)
        {
            int v = Base32Alphabet.IndexOf(c);
            if (v < 0)
                throw new FormatException($"'{c}' is not a base32 character in secret '{s}'");
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
