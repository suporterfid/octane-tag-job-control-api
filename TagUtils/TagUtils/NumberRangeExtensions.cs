using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Impinj.TagUtils
{
    public static class NumberRangeExtensions
    {
        public static long ToInt64(this string value)
        {
            string str = !string.IsNullOrEmpty(value) ? value.Trim() : throw new ArgumentNullException(nameof(value));
            return str.StartsWith("0x") ? long.Parse(str.Substring(2), NumberStyles.HexNumber) : Convert.ToInt64(str);
        }

        public static string ToBinary(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            string lower = value.ToLower();
            string str = (lower.ToLower().StartsWith("0x") ? lower.Substring(2) : lower).Replace(" ", string.Empty).Replace("-", string.Empty);
            string invalidChars = (string)str.Clone();
            (new string[17]
            {
        "0",
        "1",
        "2",
        "3",
        "4",
        "5",
        "6",
        "7",
        "8",
        "9",
        "a",
        "b",
        "c",
        "d",
        "e",
        "f",
        "?"
            }).All(c =>
            {
                invalidChars = invalidChars.Replace(c, string.Empty);
                return true;
            });
            if (!string.IsNullOrWhiteSpace(invalidChars))
                throw new ArgumentException("Error Converting string '" + value + "' to binary! Only hex numbers (including those beginning with '0x') are supported!");
            StringBuilder stringBuilder = new StringBuilder();
            foreach (char ch in str.ToCharArray())
                stringBuilder.Append(ch.ToString().Contains("?") ? "????" : Convert.ToString(Convert.ToInt32(ch.ToString(), 16), 2).PadLeft(4, '0'));
            return stringBuilder.ToString();
        }

        public static string AddPrefix(this string value, int numberSystem)
        {
            if (!string.IsNullOrEmpty(value))
            {
                switch (numberSystem)
                {
                    case 2:
                        return value.StartsWith("0b") ? value : "0b" + value;
                    case 16:
                        return value.StartsWith("0x") ? value : "0x" + value;
                }
            }
            return value;
        }

        public static string ToHex(this string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            string invalidChars = (string)(value.ToLower().StartsWith("0b") ? value.Substring(2) : value).Replace(" ", string.Empty).Replace("-", string.Empty).Clone();
            (new string[3] { "0", "1", "?" }).All(c =>
            {
                invalidChars = invalidChars.Replace(c, string.Empty);
                return true;
            });
            if (!string.IsNullOrWhiteSpace(invalidChars))
                throw new ArgumentException("Error Converting string to hex! Only binary numbers (including those beginning with '0b') are supported!");
            if (value.Length % 4 != 0)
                value = value.PadLeft((value.Length / 4 + 1) * 4, '0');
            string hex = string.Join(string.Empty, Enumerable.Range(0, value.Length / 4).Select(i => !value.Substring(i * 4, 4).Contains("?") ? Convert.ToByte(value.Substring(i * 4, 4), 2).ToString("X") : "?"));
            if (string.IsNullOrEmpty(hex))
                hex = "?";
            return hex;
        }

        public static string InsertHexValue(
          this List<NumberRange> targetRanges,
          string targetHex,
          string sourceHex)
        {
            int totalWidth = (int)targetRanges.BitCount();
            string binary = targetHex.ToBinary();
            string str1 = sourceHex.ToBinary().PadLeft(totalWidth, '?');
            string str2 = str1.Substring(Math.Max(0, str1.Length - totalWidth));
            int startIndex1 = 0;
            int startIndex2 = 0;
            StringBuilder stringBuilder = new StringBuilder();
            foreach (NumberRange targetRange in targetRanges)
            {
                int num1 = targetRange.Min < binary.Length ? (int)targetRange.Min : binary.Length - 1;
                int num2 = targetRange.Max < binary.Length ? (int)targetRange.Max : binary.Length - 1;
                int num3 = num2 - num1 + 1;
                int num4 = startIndex2;
                int num5 = startIndex2 + num3 - 1 < str2.Length ? startIndex2 + num3 - 1 : str2.Length - 1;
                int num6 = num5 - num4 + 1;
                if (num3 > 0 && num1 - 1 > startIndex1 && num1 - 1 < binary.Length)
                    stringBuilder.Append(binary, startIndex1, num1 - startIndex1);
                if (num6 > 0)
                    stringBuilder.Append(str2.Substring(startIndex2, num5 - num4 + 1));
                if (num6 < num3)
                    stringBuilder.Append(binary.Substring(startIndex1 + num6 - 1, num3 - num6));
                startIndex1 = num2 + 1;
                startIndex2 = num5 + 1;
            }
            stringBuilder.Append(binary.Substring(startIndex1));
            return stringBuilder.ToString().ToHex();
        }

        public static string ApplyToHexValue(this List<NumberRange> numberRanges, string hex)
        {
            string binary = hex.ToBinary();
            StringBuilder stringBuilder = new StringBuilder();
            foreach (NumberRange numberRange in numberRanges)
            {
                int num = numberRange.Max < binary.Length ? (int)numberRange.Max : binary.Length - 1;
                int startIndex = numberRange.Min < binary.Length ? (int)numberRange.Min : binary.Length - 1;
                stringBuilder.Append(binary.Substring(startIndex, num - startIndex + 1));
            }
            return stringBuilder.ToString().ToHex();
        }

        public static long BitCount(this List<NumberRange> numberRanges) => numberRanges.Sum(n => n.Count);

        public static List<NumberRange> AddFrom(
          this List<NumberRange> numberRanges,
          string strNumberRanges)
        {
            if (string.IsNullOrWhiteSpace(strNumberRanges))
                return null;
            foreach (string strNumberRange in strNumberRanges.Trim().Split(",".ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
                numberRanges.Add(NumberRange.FromString(strNumberRange));
            return numberRanges;
        }

        public static List<int> ToRangeList(this List<NumberRange> numberRanges)
        {
            List<int> rangeList = new List<int>();
            foreach (NumberRange numberRange in numberRanges)
                rangeList.AddRange(Enumerable.Range((int)numberRange.Min, (int)numberRange.Count));
            return rangeList;
        }

        public static string ToHexString(this List<NumberRange> numberRanges) => string.Join(",", numberRanges.Select(e => e.ToHexString()));

        public static string ToString(this List<NumberRange> numberRanges) => string.Join(",", numberRanges.Select(e => e.ToString()));
    }
}
