using System.Security.Cryptography;

namespace ZitiDesktopEdge.UITests.MockIpc;

public static class Totp
{
    private const int Digits = 6;

    public static string Compute(string base32Secret, DateTimeOffset utc)
    {
        string base32Text = base32Secret.TrimEnd('=').ToUpperInvariant();
        List<byte> key = new List<byte>((base32Text.Length * 5 + 7) / 8);
        int buffer = 0, bits = 0;
        foreach (char symbol in base32Text)
        {
            int symbolValue = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(symbol);
            if (symbolValue < 0)
                throw new FormatException($"'{symbol}' is not a base32 character in secret '{base32Secret}'");
            buffer = (buffer << 5) | symbolValue;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                key.Add((byte)((buffer >> bits) & 0xFF));
            }
        }
        long counter = utc.ToUnixTimeSeconds() / 30;
        byte[] counterBytes = new byte[8];
        for (int byteIndex = 7; byteIndex >= 0; byteIndex--) { counterBytes[byteIndex] = (byte)(counter & 0xFF); counter >>= 8; }
        using HMACSHA1 hmac = new HMACSHA1(key.ToArray());
        byte[] hash = hmac.ComputeHash(counterBytes);
        int offset = hash[hash.Length - 1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);
        int otp = binary % (int)Math.Pow(10, Digits);
        return otp.ToString(new string('0', Digits));
    }
}
