using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NetFoundry.VPN.Util
{
    public static class BinaryVisualiser
    {
        public static string FormatAsHex([System.Runtime.InteropServices.WindowsRuntime.ReadOnlyArrayAttribute]
            byte[] dataAsBytes, int len)
        {
            ReadOnlySpan<byte> data = new ReadOnlySpan<byte>(dataAsBytes);

            byte ReplaceControlCharacterWithDot(byte character)
                => character < 31 || character >= 127 ? (byte)46 /* dot */ : character;

            byte[] ReplaceControlCharactersWithDots(byte[] characters)
                => characters.Select(ReplaceControlCharacterWithDot).ToArray();

            var result = new StringBuilder();
            const int lineWidth = 16;
            for (var pos = 0; pos < len;)
            {
                var line = data.Slice(pos, Math.Min(lineWidth, data.Length - pos)).ToArray();
                var asHex = string.Join(" ",
                    line.Select(v => v.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));
                asHex += new string(' ', lineWidth * 3 - 1 - asHex.Length);
                var asCharacters = Encoding.ASCII.GetString(ReplaceControlCharactersWithDots(line));
                result.Append(FormattableString.Invariant($"{pos:X4} {asHex} {asCharacters}\n"));
                if (line.Length > 0)
                {
                    pos += line.Length;
                }
                else
                {
                    break;
                }
            }

            return result.ToString();
        }
    }
}
