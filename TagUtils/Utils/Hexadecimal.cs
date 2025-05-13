using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;


#nullable enable
namespace Impinj.Utils
{
    public class Hexadecimal
    {
        private
#nullable disable
        StringBuilder sbValue = new StringBuilder();

        public int Length => sbValue.Length;

        public Hexadecimal(string value)
        {
            string str1 = !string.IsNullOrWhiteSpace(value) ? value.Trim().ToLower() : throw new ArgumentNullException(nameof(value));
            string source = (str1.StartsWith("0x") ? str1.Substring(2) : str1).Replace(" ", string.Empty).Replace("-", string.Empty);
            string str2 = "Error Converting string '" + value + "' to hexadecimal!";
            if (source.Any(c =>
            {
                if (c >= '0' && c <= '9')
                    return false;
                return c < 'a' || c > 'f';
            }))
                throw new ArgumentException(str2 + " Only hex digits (including those beginning with '0x') are supported!");
            sbValue = source.All(c =>
            {
                if (c >= '0' && c <= '9')
                    return true;
                return c >= 'a' && c <= 'f';
            }) ? new StringBuilder(source) : throw new ArgumentNullException(str2 + " No Hexadecimal characters found!");
        }

        public static implicit operator string(Hexadecimal d) => d.ToString();

        public static implicit operator Hexadecimal(string s) => new Hexadecimal(s);

        public static implicit operator BitArray(Hexadecimal d) => d.ToBitArray();

        public static implicit operator long(Hexadecimal d) => long.Parse(d.ToRawHexString(), NumberStyles.HexNumber);

        public static implicit operator Hexadecimal(long b) => new Hexadecimal(b.ToString("X"));

        public static implicit operator int(Hexadecimal d) => int.Parse(d.ToRawHexString(), NumberStyles.HexNumber);

        public static implicit operator Hexadecimal(int b) => new Hexadecimal(b.ToString("X"));

        public override string ToString()
        {
            DefaultInterpolatedStringHandler interpolatedStringHandler = new DefaultInterpolatedStringHandler(2, 1);
            interpolatedStringHandler.AppendLiteral("0x");
            interpolatedStringHandler.AppendFormatted(ToRawHexString(), "X2");
            return interpolatedStringHandler.ToStringAndClear();
        }

        public string ToRawHexString() => sbValue.ToString();

        public BitArray ToBitArray() => new BitArray(Enumerable.Range(0, sbValue.Length).SelectMany(x => new bool[4]
        {
      ( Convert.ToByte(sbValue.ToString(x, 1), 16) & 8U) > 0U,
      ( Convert.ToByte(sbValue.ToString(x, 1), 16) & 4U) > 0U,
      ( Convert.ToByte(sbValue.ToString(x, 1), 16) & 2U) > 0U,
      ( Convert.ToByte(sbValue.ToString(x, 1), 16) & 1U) > 0U
        }).ToArray());
    }
}
