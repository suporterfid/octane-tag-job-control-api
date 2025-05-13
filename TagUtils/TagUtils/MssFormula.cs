using Impinj.Utils;
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Impinj.TagUtils
{
    public class MssFormula
    {
        private static string ZeroedOut96BitValue = string.Empty.PadLeft(96, '0');

        public static string GenerateMssSerialNumberWithSerializationPrefix(
          TagSample tagSample,
          string prefix)
        {
            string binary = tagSample.TID.ToBinary();
            if (binary.Length != 96)
                throw new ArgumentException("Length not equal to 96-bits", "tid");
            string str = string.Empty.PadLeft(38, '0');
            try
            {
                switch (tagSample.Type.FromEnumMemberAttrValue<ETagType>())
                {
                    case ETagType.ImpinjMonzaX2K:
                    case ETagType.ImpinjMonzaX8K:
                        str = GenSerialNumberMonzaX(binary);
                        break;
                    case ETagType.ImpinjMonza4D:
                    case ETagType.ImpinjMonza4E:
                    case ETagType.ImpinjMonza4QT:
                    case ETagType.ImpinjMonza4i:
                        str = GenSerialNumberMonza4(binary);
                        break;
                    case ETagType.ImpinjMonza5:
                        str = GenSerialNumberMonza5(binary);
                        break;
                    case ETagType.ImpinjMonzaR6:
                    case ETagType.ImpinjMonzaR6P:
                    case ETagType.ImpinjMonzaR6A:
                    case ETagType.ImpinjMonzaR6B:
                        str = GenSerialNumberMonza6(binary);
                        break;
                    case ETagType.ImpinjM750:
                    case ETagType.ImpinjM730:
                        str = GenSerialNumberImpinjM700(binary);
                        break;
                    default:
                        str = ZeroedOut96BitValue;
                        break;
                }
                if (IsValidSerializationPrefix(prefix))
                    str = prefix + str.Substring(prefix.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return str.ToHex();
        }

        public static string GenerateMss96BitSerialNumber(TagSample tagSample)
        {
            string binary = tagSample.TID.ToBinary();
            if (binary.Length != 96)
                throw new ArgumentException("Length not equal to 96-bits", "tid");
            string str = ZeroedOut96BitValue;
            try
            {
                switch (tagSample.Type.FromEnumMemberAttrValue<ETagType>())
                {
                    case ETagType.ImpinjMonzaX2K:
                    case ETagType.ImpinjMonzaX8K:
                        str = Gen96BitSerialNumberMonzaX(binary);
                        break;
                    case ETagType.ImpinjMonza4D:
                    case ETagType.ImpinjMonza4E:
                    case ETagType.ImpinjMonza4QT:
                    case ETagType.ImpinjMonza4i:
                        str = Gen96BitSerialNumberMonza4(binary);
                        break;
                    case ETagType.ImpinjMonza5:
                        str = Gen96BitSerialNumberMonza5(binary);
                        break;
                    case ETagType.ImpinjMonzaR6:
                    case ETagType.ImpinjMonzaR6P:
                    case ETagType.ImpinjMonzaR6A:
                    case ETagType.ImpinjMonzaR6B:
                        str = Gen96BitSerialNumberMonza6(binary);
                        break;
                    case ETagType.ImpinjM750:
                    case ETagType.ImpinjM730:
                        str = Gen96BitSerialNumberImpinjM700(binary);
                        break;
                    default:
                        str = ZeroedOut96BitValue;
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return str.ToHex();
        }

        public static string Mss96BitSerialNumberToTid(TagSample tagSample)
        {
            string binary = tagSample.EPC.ToBinary();
            if (binary.Length != 96)
                throw new ArgumentException("Length not equal to 96-bits", "epc");
            TagSample tagSample1 = new TagSample(tagSample.Library, tagSample.EPC, tagSample.TID);
            tagSample1.RefreshFields();
            string str = ZeroedOut96BitValue;
            try
            {
                switch (tagSample1.Type.FromEnumMemberAttrValue<ETagType>())
                {
                    case ETagType.ImpinjMonzaR6:
                    case ETagType.ImpinjMonzaR6P:
                    case ETagType.ImpinjMonzaR6A:
                    case ETagType.ImpinjMonzaR6B:
                        str = Mss96BitSerialNumberToTidMonza6(binary);
                        break;
                    case ETagType.ImpinjM750:
                    case ETagType.ImpinjM730:
                        str = Mss96BitSerialNumberToTidImpinjM700(binary);
                        break;
                    default:
                        str = ZeroedOut96BitValue;
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return str.ToHex();
        }

        public static string GenSerialNumberImpinjM700(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(85, 2), 2);
            StringBuilder stringBuilder = new StringBuilder(38);
            if (int64 <= 3L)
            {
                stringBuilder.Append(binaryTID.Substring(87, 9));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(51, 13));
            }
            return stringBuilder.ToString();
        }

        public static string Gen96BitSerialNumberImpinjM700(string binaryTID)
        {
            long int64_1 = Convert.ToInt64(binaryTID.Substring(85, 2), 2);
            long int64_2 = Convert.ToInt64(binaryTID.Substring(48, 3), 2);
            StringBuilder stringBuilder = new StringBuilder(96);
            if (int64_1 <= 3L)
            {
                stringBuilder.Append(binaryTID.Substring(0, 16));
                stringBuilder.Append(binaryTID.Substring(16, 16));
                if (int64_2 >= 2L)
                {
                    stringBuilder.Append("1010010100000");
                    stringBuilder.Append(binaryTID.Substring(48, 3));
                }
                else
                {
                    stringBuilder.Append(binaryTID.Substring(48, 3));
                    stringBuilder.Append("0000000000000");
                }
                stringBuilder.Append("000");
                stringBuilder.Append(binaryTID.Substring(80, 7));
                stringBuilder.Append(binaryTID.Substring(87, 9));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(51, 13));
            }
            return stringBuilder.ToString();
        }

        public static string Mss96BitSerialNumberToTidImpinjM700(string binaryEPC)
        {
            long int64_1 = Convert.ToInt64(binaryEPC.Substring(53, 2), 2);
            long int64_2 = Convert.ToInt64(binaryEPC.Substring(35, 13), 2);
            string str = binaryEPC.Substring(int64_2 == 0L ? 32 : 45, 3);
            StringBuilder stringBuilder = new StringBuilder(96);
            if (int64_1 <= 3L)
            {
                stringBuilder.Append(binaryEPC.Substring(0, 16));
                stringBuilder.Append(binaryEPC.Substring(16, 16));
                stringBuilder.Append("0010000000000000");
                stringBuilder.Append(str);
                stringBuilder.Append(binaryEPC.Substring(83, 13));
                stringBuilder.Append(binaryEPC.Substring(67, 13));
                stringBuilder.Append(binaryEPC.Substring(80, 3));
                stringBuilder.Append(binaryEPC.Substring(51, 13));
                stringBuilder.Append(binaryEPC.Substring(64, 3));
            }
            return stringBuilder.ToString();
        }

        public static string GenSerialNumberMonza6(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(80, 5), 2);
            StringBuilder stringBuilder = new StringBuilder(38);
            if (int64 <= 3L)
            {
                stringBuilder.Append(binaryTID.Substring(85, 11));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(53, 11));
            }
            return stringBuilder.ToString();
        }

        public static string Gen96BitSerialNumberMonza6(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(80, 5), 2);
            StringBuilder stringBuilder = new StringBuilder(96);
            if (int64 <= 31L)
            {
                stringBuilder.Append(binaryTID.Substring(0, 16));
                stringBuilder.Append(binaryTID.Substring(16, 16));
                stringBuilder.Append(binaryTID.Substring(48, 3));
                stringBuilder.Append("0");
                stringBuilder.Append(binaryTID.Substring(80, 3));
                stringBuilder.Append("00000000000000");
                stringBuilder.Append(binaryTID.Substring(83, 2));
                stringBuilder.Append("00");
                stringBuilder.Append(binaryTID.Substring(52, 1));
                stringBuilder.Append(binaryTID.Substring(85, 11));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(53, 11));
            }
            return stringBuilder.ToString();
        }

        public static string Mss96BitSerialNumberToTidMonza6(string binaryEPC)
        {
            long int64 = Convert.ToInt64(binaryEPC.Substring(53, 2), 2);
            StringBuilder stringBuilder1 = new StringBuilder(47);
            stringBuilder1.Append(binaryEPC.Substring(32, 3));
            stringBuilder1.Append(binaryEPC.Substring(57, 1));
            stringBuilder1.Append(binaryEPC.Substring(85, 11));
            stringBuilder1.Append(binaryEPC.Substring(69, 16));
            stringBuilder1.Append(binaryEPC.Substring(36, 3));
            stringBuilder1.Append(binaryEPC.Substring(53, 2));
            stringBuilder1.Append(binaryEPC.Substring(58, 11));
            StringBuilder stringBuilder2 = new StringBuilder(96);
            if (int64 <= 3L)
            {
                stringBuilder2.Append(binaryEPC.Substring(0, 16));
                stringBuilder2.Append(binaryEPC.Substring(16, 16));
                stringBuilder2.Append("0010000000000000");
                stringBuilder2.Append(binaryEPC.Substring(32, 3));
                stringBuilder2.Append(ParityCheck(stringBuilder1.ToString()));
                stringBuilder2.Append(binaryEPC.Substring(57, 1));
                stringBuilder2.Append(binaryEPC.Substring(85, 11));
                stringBuilder2.Append(binaryEPC.Substring(69, 16));
                stringBuilder2.Append(binaryEPC.Substring(36, 3));
                stringBuilder2.Append(binaryEPC.Substring(53, 2));
                stringBuilder2.Append(binaryEPC.Substring(58, 11));
            }
            return stringBuilder2.ToString();
        }

        public static string GenSerialNumberMonza5(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(80, 5), 2);
            StringBuilder stringBuilder = new StringBuilder(38);
            if (int64 > 0L && int64 <= 3L)
            {
                stringBuilder.Append(binaryTID.Substring(85, 11));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(52, 2));
                stringBuilder.Append(binaryTID.Substring(55, 1));
                stringBuilder.Append(binaryTID.Substring(56, 8));
            }
            else
            {
                stringBuilder.Append("000");
                stringBuilder.Append(binaryTID.Substring(85, 8));
                stringBuilder.Append(binaryTID.Substring(65, 15));
                stringBuilder.Append(binaryTID.Substring(93, 3));
                stringBuilder.Append(binaryTID.Substring(64, 1));
                stringBuilder.Append(binaryTID.Substring(56, 8));
            }
            return stringBuilder.ToString();
        }

        public static string Gen96BitSerialNumberMonza5(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(80, 5), 2);
            StringBuilder stringBuilder = new StringBuilder(96);
            stringBuilder.Append(binaryTID.Substring(0, 16));
            stringBuilder.Append(binaryTID.Substring(16, 16));
            stringBuilder.Append(binaryTID.Substring(48, 4));
            stringBuilder.Append(binaryTID.Substring(80, 3));
            stringBuilder.Append("00000000000000");
            stringBuilder.Append(binaryTID.Substring(83, 2));
            stringBuilder.Append("000");
            if (int64 >= 1L && int64 <= 3L)
            {
                stringBuilder.Append(binaryTID.Substring(85, 11));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(52, 2));
                stringBuilder.Append(binaryTID.Substring(55, 1));
                stringBuilder.Append(binaryTID.Substring(56, 8));
            }
            else
            {
                stringBuilder.Append("000");
                stringBuilder.Append(binaryTID.Substring(85, 8));
                stringBuilder.Append(binaryTID.Substring(65, 15));
                stringBuilder.Append(binaryTID.Substring(93, 3));
                stringBuilder.Append(binaryTID.Substring(64, 1));
                stringBuilder.Append(binaryTID.Substring(56, 8));
            }
            return stringBuilder.ToString();
        }

        public static string GenSerialNumberMonza4(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(80, 5), 2);
            StringBuilder stringBuilder = new StringBuilder(38);
            if (int64 > 0L && int64 <= 3L)
            {
                stringBuilder.Append(binaryTID.Substring(85, 11));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(52, 2));
                stringBuilder.Append(binaryTID.Substring(59, 1));
                stringBuilder.Append(binaryTID.Substring(54, 4));
                stringBuilder.Append(binaryTID.Substring(60, 4));
            }
            else
            {
                stringBuilder.Append("000");
                stringBuilder.Append(binaryTID.Substring(85, 8));
                stringBuilder.Append(binaryTID.Substring(65, 15));
                stringBuilder.Append(binaryTID.Substring(93, 3));
                stringBuilder.Append(binaryTID.Substring(64, 1));
                stringBuilder.Append(binaryTID.Substring(54, 4));
                stringBuilder.Append(binaryTID.Substring(60, 4));
            }
            return stringBuilder.ToString();
        }

        public static string Gen96BitSerialNumberMonza4(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(80, 5), 2);
            StringBuilder stringBuilder = new StringBuilder(96);
            stringBuilder.Append(binaryTID.Substring(0, 16));
            stringBuilder.Append(binaryTID.Substring(16, 16));
            stringBuilder.Append(binaryTID.Substring(48, 4));
            stringBuilder.Append(binaryTID.Substring(80, 3));
            stringBuilder.Append("00000000000000");
            stringBuilder.Append(binaryTID.Substring(83, 2));
            stringBuilder.Append("000");
            if (int64 >= 1L && int64 <= 3L)
            {
                stringBuilder.Append(binaryTID.Substring(85, 11));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(52, 2));
                stringBuilder.Append(binaryTID.Substring(59, 1));
                stringBuilder.Append(binaryTID.Substring(54, 4));
                stringBuilder.Append(binaryTID.Substring(60, 4));
            }
            else
            {
                stringBuilder.Append("000");
                stringBuilder.Append(binaryTID.Substring(85, 8));
                stringBuilder.Append(binaryTID.Substring(65, 15));
                stringBuilder.Append(binaryTID.Substring(93, 3));
                stringBuilder.Append(binaryTID.Substring(64, 1));
                stringBuilder.Append(binaryTID.Substring(54, 4));
                stringBuilder.Append(binaryTID.Substring(60, 4));
            }
            return stringBuilder.ToString();
        }

        public static string GenSerialNumberMonzaX(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(80, 5), 2);
            StringBuilder stringBuilder = new StringBuilder(38);
            if (int64 <= 3L && int64 >= 1L)
            {
                stringBuilder.Append(binaryTID.Substring(85, 11));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(52, 2));
                stringBuilder.Append(binaryTID.Substring(55, 9));
            }
            return stringBuilder.ToString();
        }

        public static string Gen96BitSerialNumberMonzaX(string binaryTID)
        {
            long int64 = Convert.ToInt64(binaryTID.Substring(80, 5), 2);
            StringBuilder stringBuilder = new StringBuilder(96);
            if (int64 <= 3L && int64 >= 1L)
            {
                stringBuilder.Append(binaryTID.Substring(0, 16));
                stringBuilder.Append(binaryTID.Substring(16, 16));
                stringBuilder.Append(binaryTID.Substring(48, 4));
                stringBuilder.Append(binaryTID.Substring(80, 3));
                stringBuilder.Append("00000000000000");
                stringBuilder.Append(binaryTID.Substring(83, 2));
                stringBuilder.Append("000");
                stringBuilder.Append(binaryTID.Substring(85, 11));
                stringBuilder.Append(binaryTID.Substring(64, 16));
                stringBuilder.Append(binaryTID.Substring(52, 2));
                stringBuilder.Append(binaryTID.Substring(55, 9));
            }
            return stringBuilder.ToString();
        }

        public static string HexStringToBinString(string InputString)
        {
            StringBuilder stringBuilder1 = new StringBuilder();
            StringBuilder stringBuilder2 = new StringBuilder();
            if (string.IsNullOrEmpty(InputString))
                throw new ArgumentNullException();
            stringBuilder2.Append(InputString);
            stringBuilder2.Replace(" ", string.Empty);
            stringBuilder2.Replace("-", string.Empty);
            if (!IsHexString(stringBuilder2.ToString()))
                throw new ArgumentException("Provided string not in hexadecimal format");
            for (int startIndex = 0; startIndex < stringBuilder2.Length; ++startIndex)
            {
                int int32 = Convert.ToInt32(stringBuilder2.ToString().Substring(startIndex, 1), 16);
                stringBuilder1.Append(Convert.ToString(int32, 2).PadLeft(4, '0'));
            }
            return stringBuilder1.ToString();
        }

        public static string BinStringToHexString(string InputString)
        {
            StringBuilder stringBuilder1 = new StringBuilder();
            StringBuilder stringBuilder2 = new StringBuilder();
            if (string.IsNullOrEmpty(InputString))
                throw new ArgumentNullException();
            stringBuilder2.Append(InputString);
            stringBuilder2.Replace(" ", string.Empty);
            stringBuilder2.Replace("-", string.Empty);
            if (stringBuilder2.Length % 4 != 0)
            {
                int num = 0;
                while (stringBuilder2.Length % 4 != 0)
                {
                    stringBuilder2.Insert(0, '0');
                    ++num;
                }
            }
            if (!OnlyBinInString(stringBuilder2.ToString()))
                throw new ArgumentException("Provided string not in binary format");
            string empty = string.Empty;
            for (int startIndex = 0; startIndex <= stringBuilder2.Length - 4; startIndex += 4)
            {
                string str = stringBuilder2.ToString().Substring(startIndex, 4);
                stringBuilder1.Append(string.Format("{0:X}", Convert.ToByte(str, 2)));
            }
            return stringBuilder1.ToString();
        }

        public static long Bin2Dec(string data) => Convert.ToInt64(data, 2);

        public static bool IsHexString(string InputString)
        {
            StringBuilder stringBuilder = new StringBuilder();
            if (string.IsNullOrEmpty(InputString))
                throw new ArgumentNullException();
            stringBuilder.Append(InputString);
            stringBuilder.Replace("0x", string.Empty);
            stringBuilder.Replace(" ", string.Empty);
            stringBuilder.Replace("-", string.Empty);
            return Regex.IsMatch(stringBuilder.ToString(), "\\A\\b[0-9a-fA-F]+\\b\\Z");
        }

        public static bool IsDecimalString(string InputString)
        {
            StringBuilder stringBuilder = new StringBuilder();
            if (string.IsNullOrEmpty(InputString))
                throw new ArgumentNullException();
            stringBuilder.Append(InputString);
            stringBuilder.Replace("0x", string.Empty);
            stringBuilder.Replace(" ", string.Empty);
            stringBuilder.Replace("-", string.Empty);
            return Regex.IsMatch(stringBuilder.ToString(), "\\A\\b[\\d]+\\b\\Z");
        }

        public static bool OnlyBinInString(string InputString)
        {
            StringBuilder stringBuilder = new StringBuilder();
            if (string.IsNullOrEmpty(InputString))
                throw new ArgumentNullException();
            stringBuilder.Append(InputString);
            stringBuilder.Replace(" ", string.Empty);
            stringBuilder.Replace("-", string.Empty);
            return Regex.IsMatch(stringBuilder.ToString(), "\\A\\b[01]+\\b\\Z");
        }

        private static bool IsValidSerializationPrefix(string prefix) => !string.IsNullOrEmpty(prefix) && 3 >= prefix.Length && IsBinaryString(prefix);

        private static char ParityCheck(string str) => str.Replace("0", "").Replace("-", "").Length % 2 != 0 ? '1' : '0';

        private static bool IsBinaryString(string str) => str.Replace("0", "").Replace("1", "").Replace("-", "").Trim().Length == 0;
    }
}
