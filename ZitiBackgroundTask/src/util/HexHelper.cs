using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;

namespace NetFoundry.VPN.Util
{
    public sealed class HexHelper
    {

        public static string FormatAsHex([ReadOnlyArrayAttribute] byte[] dataAsBytes, int len)
        {
            return FormatAsHex(dataAsBytes, len, 16);
        }

        public static string FormatAsHex([ReadOnlyArrayAttribute] byte[] dataAsBytes, int len, byte lineWidth)
        {
            ReadOnlySpan<byte> data = new ReadOnlySpan<byte>(dataAsBytes);

            byte ReplaceControlCharacterWithDot(byte character)
                => character < 31 || character >= 127 ? (byte)46 /* dot */ : character;

            byte[] ReplaceControlCharactersWithDots(byte[] characters)
                => characters.Select(ReplaceControlCharacterWithDot).ToArray();

            var result = new StringBuilder();
            //const int lineWidth = 16;
            for (var pos = 0; pos < len;)
            {
                var howManyBytes = Math.Min(lineWidth, len - pos);
                var line = data.Slice(pos, howManyBytes).ToArray();
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

            return result.ToString().Remove(result.Length - 1);
        }

        public static byte[] FromDelimitedString(string hex, char delimiter)
        {
            string[] strBytes = hex.Split(delimiter);
            byte[] bytes = new byte[strBytes.Length];
            int i = 0;
            foreach(string b in strBytes)
            {
                bytes[i++] = Byte.Parse(b);
            }
            return bytes;
        }

        public static byte[] FromString(string hex)
        {
            if (hex.Length % 2 == 1)
                throw new Exception("The binary key cannot have an odd number of digits");
            int hexLen = hex.Length / 2;
            byte[] bytes = new byte[hexLen];
            for(int i = 0; i < hexLen; i++)
            {
                string strByte = hex.Substring(i * 2, 2);
                Debug.WriteLine("READ: " + strByte);
                bytes[i] = Byte.Parse(strByte, System.Globalization.NumberStyles.AllowHexSpecifier);
            }
            return bytes;
        }

        public static int GetHexVal(char hex)
        {
            int val = (int)hex;
            //For uppercase A-F letters:
            return val - (val < 58 ? 48 : 55);
            //For lowercase a-f letters:
            //return val - (val < 58 ? 48 : 87);
            //Or the two combined, but a bit slower:
            //return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
        }
    }
}
