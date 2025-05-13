using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Impinj.TagUtils
{
    public class Sgtin96
    {
        private const string _uriPrefix = "urn:epc:tag:sgtin-96:";
        private const ushort _header = 48;
        private ushort _filterValue = 1;
        private const ushort _filterValueMin = 0;
        private const ushort _filterValueMax = 7;
        private ushort _partition = 5;
        private const ushort _partitionValueMin = 0;
        private const ushort _partitionValueMax = 6;
        private ulong _companyPrefix;
        private int _companyPrefixLengthInBits = 24;
        private int _companyPrefixLengthInDigits = 7;
        private uint _itemReference;
        private int _itemReferenceLengthInBits = 20;
        private int _itemReferenceLengthInDigits = 6;
        private string _upc;
        private int _upcCheckDigit;
        private ulong _serialNumber = 0;
        private const int _serialNumberMaxBits = 38;
        private const ushort CONVERT_HEX = 16;
        private const ushort CONVERT_DECIMAL = 10;
        private const ushort CONVERT_BINARY = 2;

        public ushort Header => 48;

        public ushort FilterValue => _filterValue;

        public ushort Partition => _partition;

        public ulong CompanyPrefix => _companyPrefix;

        public uint ItemReference => _itemReference;

        public string UPC => _upc + _upcCheckDigit.ToString();

        public ulong SerialNumber
        {
            get => _serialNumber;
            set => _serialNumber = value;
        }

        private Sgtin96()
        {
        }

        public static Sgtin96 FromString(string Sgtin96AsString)
        {
            if (string.IsNullOrEmpty(Sgtin96AsString))
                throw new ArgumentNullException("Null or Empty SGTIN-96 string.");
            Sgtin96 sgtin96;
            if (IsValidUri(Sgtin96AsString))
            {
                try
                {
                    sgtin96 = FromSgtin96Uri(Sgtin96AsString);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            else
            {
                if (!IsValidEpc(Sgtin96AsString))
                    throw new ArgumentException("Invalid SGTIN-96 string");
                try
                {
                    sgtin96 = FromSgtin96Epc(Sgtin96AsString);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return sgtin96;
        }

        public static Sgtin96 FromSgtin96Uri(string Uri)
        {
            Sgtin96 Sgtin96ToUpdate = new Sgtin96();
            string str = Uri;
            if (!IsValidUri(Uri))
                throw new ArgumentException("Invalid SGTIN-96 URI");
            string[] strArray = str.Replace("urn:epc:tag:sgtin-96:", string.Empty).Split('.');
            Sgtin96ToUpdate._filterValue = Convert.ToUInt16(strArray[0]);
            Sgtin96ToUpdate._companyPrefix = Convert.ToUInt64(strArray[1]);
            Sgtin96ToUpdate._itemReference = Convert.ToUInt32(strArray[2]);
            Sgtin96ToUpdate._partition = (ushort)(strArray[2].Length - 1);
            SetLengthsFromPartition(ref Sgtin96ToUpdate);
            Sgtin96ToUpdate._serialNumber = Convert.ToUInt64(strArray[3]);
            string format1 = "D" + (Sgtin96ToUpdate._companyPrefixLengthInDigits - 1).ToString();
            Sgtin96ToUpdate._upc = Sgtin96ToUpdate._companyPrefix.ToString(format1);
            if (12 > Sgtin96ToUpdate._companyPrefixLengthInDigits)
            {
                string format2 = "D" + (Sgtin96ToUpdate._itemReferenceLengthInDigits - 1).ToString();
                Sgtin96ToUpdate._upc += Sgtin96ToUpdate._itemReference.ToString(format2);
            }
            CalculateUpcCheckDigit(Sgtin96ToUpdate._upc, out Sgtin96ToUpdate._upcCheckDigit);
            return Sgtin96ToUpdate;
        }

        public static Sgtin96 FromSgtin96Epc(string Epc)
        {
            Sgtin96 Sgtin96ToUpdate = new Sgtin96();
            string empty = string.Empty;
            string str1 = !string.IsNullOrEmpty(Epc) ? Epc.Replace(" ", string.Empty) : throw new ArgumentException("null SGTIN-96 EPC");
            string str2 = IsValidEpc(str1) ? MssFormula.HexStringToBinString(str1) : throw new ArgumentException("Invalid SGTIN-96 EPC");
            Sgtin96ToUpdate._filterValue = Convert.ToUInt16(str2.Substring(8, 3), 2);
            Sgtin96ToUpdate._partition = Convert.ToUInt16(str2.Substring(11, 3), 2);
            SetLengthsFromPartition(ref Sgtin96ToUpdate);
            Sgtin96ToUpdate._companyPrefix = Convert.ToUInt64(str2.Substring(14, Sgtin96ToUpdate._companyPrefixLengthInBits), 2);
            Sgtin96ToUpdate._itemReference = Convert.ToUInt32(str2.Substring(14 + Sgtin96ToUpdate._companyPrefixLengthInBits, Sgtin96ToUpdate._itemReferenceLengthInBits), 2);
            Sgtin96ToUpdate._serialNumber = Convert.ToUInt64(str2.Substring(58, 38), 2);
            string format1 = "D" + (Sgtin96ToUpdate._companyPrefixLengthInDigits - 1).ToString();
            Sgtin96ToUpdate._upc = Sgtin96ToUpdate._companyPrefix.ToString(format1);
            if (12 > Sgtin96ToUpdate._companyPrefixLengthInDigits)
            {
                string format2 = "D" + (Sgtin96ToUpdate._itemReferenceLengthInDigits - 1).ToString();
                Sgtin96ToUpdate._upc += Sgtin96ToUpdate._itemReference.ToString(format2);
            }
            CalculateUpcCheckDigit(Sgtin96ToUpdate._upc, out Sgtin96ToUpdate._upcCheckDigit);
            return Sgtin96ToUpdate;
        }

        public static Sgtin96 FromUPC(string UPC, int companyPrefixLength)
        {
            StringBuilder stringBuilder = new StringBuilder();
            if (!IsValidGtin(UPC))
                throw new ArgumentException("Invalid UPC string.");
            stringBuilder.Append("urn:epc:tag:sgtin-96:");
            stringBuilder.Append("1.");
            stringBuilder.Append(UPC.Substring(0, companyPrefixLength - 1).PadLeft(companyPrefixLength, '0'));
            stringBuilder.Append(".");
            stringBuilder.Append('0');
            stringBuilder.Append(UPC.Substring(companyPrefixLength - 1, UPC.Length - companyPrefixLength));
            stringBuilder.Append(".0");
            return FromSgtin96Uri(stringBuilder.ToString());
        }

        public static Sgtin96 FromGTIN(string Gtin, int companyPrefixLength)
        {
            Sgtin96 sgtin96 = new Sgtin96();
            string str = Gtin.Substring(1, Gtin.Length - 1);
            StringBuilder stringBuilder = new StringBuilder();
            if (!IsValidGtin(Gtin))
                throw new ArgumentException("Invalid GTIN string.");
            stringBuilder.Append("urn:epc:tag:sgtin-96:");
            stringBuilder.Append("1.");
            stringBuilder.Append(str.Substring(0, companyPrefixLength));
            stringBuilder.Append(".");
            stringBuilder.Append('0');
            int length = str.Length - companyPrefixLength - 1;
            stringBuilder.Append(str.Substring(companyPrefixLength, length));
            stringBuilder.Append(".0");
            return FromSgtin96Uri(stringBuilder.ToString());
        }

        public override string ToString() => ToUri();

        public string ToUri()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("urn:epc:tag:sgtin-96:");
            stringBuilder.Append(_filterValue);
            stringBuilder.Append(".");
            stringBuilder.Append(_companyPrefix.ToString().PadLeft(_companyPrefixLengthInDigits, '0'));
            stringBuilder.Append(".");
            stringBuilder.Append(_itemReference.ToString().PadLeft(_itemReferenceLengthInDigits, '0'));
            stringBuilder.Append(".");
            stringBuilder.Append(_serialNumber.ToString());
            return stringBuilder.ToString();
        }

        public string ToEpc()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(Convert.ToString(Convert.ToInt32((ushort)48), 2).PadLeft(8, '0'));
            stringBuilder.Append(Convert.ToString(Convert.ToInt32(_filterValue), 2).PadLeft(3, '0'));
            stringBuilder.Append(Convert.ToString(Convert.ToInt32(_partition), 2).PadLeft(3, '0'));
            stringBuilder.Append(Convert.ToString(Convert.ToInt64(_companyPrefix), 2).PadLeft(_companyPrefixLengthInBits, '0'));
            stringBuilder.Append(Convert.ToString(Convert.ToInt32(_itemReference), 2).PadLeft(_itemReferenceLengthInBits, '0'));
            stringBuilder.Append(Convert.ToString(Convert.ToInt64(_serialNumber), 2).PadLeft(38, '0'));
            return MssFormula.BinStringToHexString(stringBuilder.ToString());
        }

        public string ToUpc()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append(_upc);
            stringBuilder.Append(_upcCheckDigit.ToString());
            return stringBuilder.ToString();
        }

        public static bool IsValidSGTIN(string testString)
        {
            bool flag = false;
            if (!string.IsNullOrEmpty(testString))
                flag = IsValidUri(testString) || IsValidEpc(testString);
            return flag;
        }

        public static string GetGTIN(string inputSGTIN)
        {
            string empty = string.Empty;
            if (string.IsNullOrEmpty(inputSGTIN))
                throw new ArgumentNullException("Null or Empty SGTIN-96 string.");
            string uri;
            if (IsValidUri(inputSGTIN))
            {
                try
                {
                    Sgtin96 sgtin96 = FromSgtin96Uri(inputSGTIN);
                    sgtin96._serialNumber = 0UL;
                    uri = sgtin96.ToUri();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            else
            {
                if (!IsValidEpc(inputSGTIN))
                    throw new ArgumentException("Invalid SGTIN-96 string");
                try
                {
                    Sgtin96 sgtin96 = FromSgtin96Epc(inputSGTIN);
                    sgtin96._serialNumber = 0UL;
                    uri = sgtin96.ToUri();
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
            return uri;
        }

        public string GetSGTINZeroValueSerialNumber()
        {
            string empty = string.Empty;
            ulong serialNumber = _serialNumber;
            _serialNumber = 0UL;
            string uri = ToUri();
            _serialNumber = serialNumber;
            return uri;
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            Sgtin96 sgtin96 = obj as Sgtin96;
            return (object)sgtin96 != null && _filterValue == sgtin96._filterValue && _partition == sgtin96._partition && (long)_companyPrefix == (long)sgtin96._companyPrefix && _companyPrefixLengthInBits == sgtin96._companyPrefixLengthInBits && _companyPrefixLengthInDigits == sgtin96._companyPrefixLengthInDigits && (int)_itemReference == (int)sgtin96._itemReference && _itemReferenceLengthInBits == sgtin96._itemReferenceLengthInBits && _itemReferenceLengthInDigits == sgtin96._itemReferenceLengthInDigits && (long)_serialNumber == (long)sgtin96._serialNumber;
        }

        public static bool operator ==(Sgtin96 a, Sgtin96 b)
        {
            if (a == (object)b)
                return true;
            return (object)a != null && (object)b != null && a._filterValue == b._filterValue && a._partition == b._partition && (long)a._companyPrefix == (long)b._companyPrefix && a._companyPrefixLengthInBits == b._companyPrefixLengthInBits && a._companyPrefixLengthInDigits == b._companyPrefixLengthInDigits && (int)a._itemReference == (int)b._itemReference && a._itemReferenceLengthInBits == b._itemReferenceLengthInBits && a._itemReferenceLengthInDigits == b._itemReferenceLengthInDigits && (long)a._serialNumber == (long)b._serialNumber;
        }

        public static bool operator !=(Sgtin96 a, Sgtin96 b) => !(a == b);

        public override int GetHashCode() => base.GetHashCode() ^ _filterValue;

        private static bool IsValidUri(string CandidateSgtin)
        {
            bool flag1 = false;
            string empty = string.Empty;
            if (!string.IsNullOrEmpty(CandidateSgtin))
            {
                string[] strArray = CandidateSgtin.Replace("urn:epc:tag:sgtin-96:", string.Empty).Split('.');
                bool flag2 = false;
                if (4 == strArray.Length)
                {
                    Regex regex = new Regex("[\\d]+");
                    foreach (string input in strArray)
                    {
                        if (!regex.IsMatch(input))
                            flag2 = true;
                    }
                    if (!flag2)
                    {
                        ushort uint16 = Convert.ToUInt16(strArray[0], 10);
                        if (0 <= uint16 && 7 >= uint16)
                        {
                            int length1 = strArray[1].Length;
                            int length2 = strArray[2].Length;
                            bool flag3 = false;
                            if (6 <= length1 && 12 >= length1 && 13 == length1 + length2)
                                flag3 = true;
                            if (flag3)
                            {
                                if (38 >= MssFormula.HexStringToBinString(Convert.ToUInt64(strArray[3], 10).ToString("X10")).TrimStart('0').Length)
                                    flag1 = true;
                            }
                        }
                    }
                }
            }
            return flag1;
        }

        private static bool IsValidEpc(string CandidateSgtin)
        {
            bool flag = false;
            if (!string.IsNullOrEmpty(CandidateSgtin) && 24 == CandidateSgtin.Length)
            {
                string binString = MssFormula.HexStringToBinString(CandidateSgtin);
                if (96 != binString.Length)
                    throw new ArgumentException("Invalid SGTIN-96 EPC");
                if (48 == Convert.ToUInt16(binString.Substring(0, 8), 2))
                {
                    ushort uint16_1 = Convert.ToUInt16(binString.Substring(8, 3), 2);
                    if (0 <= uint16_1 && 7 >= uint16_1)
                    {
                        ushort uint16_2 = Convert.ToUInt16(binString.Substring(11, 3), 2);
                        if (0 <= uint16_2 && 6 >= uint16_2)
                            flag = true;
                    }
                }
            }
            return flag;
        }

        private static void CalculateUpcCheckDigit(string UPC, out int checkDigit)
        {
            int num = 0;
            if (UPC == new Regex("[^0-9]").Replace(UPC, ""))
            {
                UPC = UPC.PadLeft(13, '0');
                int[] numArray = new int[13]
                {
          int.Parse(UPC[0].ToString()) * 3,
          int.Parse(UPC[1].ToString()),
          int.Parse(UPC[2].ToString()) * 3,
          int.Parse(UPC[3].ToString()),
          int.Parse(UPC[4].ToString()) * 3,
          int.Parse(UPC[5].ToString()),
          int.Parse(UPC[6].ToString()) * 3,
          int.Parse(UPC[7].ToString()),
          int.Parse(UPC[8].ToString()) * 3,
          int.Parse(UPC[9].ToString()),
          int.Parse(UPC[10].ToString()) * 3,
          int.Parse(UPC[11].ToString()),
          int.Parse(UPC[12].ToString()) * 3
                };
                num = (10 - (numArray[0] + numArray[1] + numArray[2] + numArray[3] + numArray[4] + numArray[5] + numArray[6] + numArray[7] + numArray[8] + numArray[9] + numArray[10] + numArray[11] + numArray[12]) % 10) % 10;
            }
            checkDigit = num;
        }

        public static bool IsValidGtin(string code)
        {
            if (code != new Regex("[^0-9]").Replace(code, ""))
                return false;
            switch (code.Length)
            {
                case 8:
                    code = "000000" + code;
                    goto case 14;
                case 12:
                    code = "00" + code;
                    goto case 14;
                case 13:
                    code = "0" + code;
                    goto case 14;
                case 14:
                    int[] numArray1 = new int[13];
                    numArray1[0] = int.Parse(code[0].ToString()) * 3;
                    numArray1[1] = int.Parse(code[1].ToString());
                    numArray1[2] = int.Parse(code[2].ToString()) * 3;
                    int[] numArray2 = numArray1;
                    char ch = code[3];
                    int num1 = int.Parse(ch.ToString());
                    numArray2[3] = num1;
                    int[] numArray3 = numArray1;
                    ch = code[4];
                    int num2 = int.Parse(ch.ToString()) * 3;
                    numArray3[4] = num2;
                    int[] numArray4 = numArray1;
                    ch = code[5];
                    int num3 = int.Parse(ch.ToString());
                    numArray4[5] = num3;
                    int[] numArray5 = numArray1;
                    ch = code[6];
                    int num4 = int.Parse(ch.ToString()) * 3;
                    numArray5[6] = num4;
                    int[] numArray6 = numArray1;
                    ch = code[7];
                    int num5 = int.Parse(ch.ToString());
                    numArray6[7] = num5;
                    int[] numArray7 = numArray1;
                    ch = code[8];
                    int num6 = int.Parse(ch.ToString()) * 3;
                    numArray7[8] = num6;
                    int[] numArray8 = numArray1;
                    ch = code[9];
                    int num7 = int.Parse(ch.ToString());
                    numArray8[9] = num7;
                    int[] numArray9 = numArray1;
                    ch = code[10];
                    int num8 = int.Parse(ch.ToString()) * 3;
                    numArray9[10] = num8;
                    int[] numArray10 = numArray1;
                    ch = code[11];
                    int num9 = int.Parse(ch.ToString());
                    numArray10[11] = num9;
                    int[] numArray11 = numArray1;
                    ch = code[12];
                    int num10 = int.Parse(ch.ToString()) * 3;
                    numArray11[12] = num10;
                    int num11 = (10 - (numArray1[0] + numArray1[1] + numArray1[2] + numArray1[3] + numArray1[4] + numArray1[5] + numArray1[6] + numArray1[7] + numArray1[8] + numArray1[9] + numArray1[10] + numArray1[11] + numArray1[12]) % 10) % 10;
                    ch = code[13];
                    int num12 = int.Parse(ch.ToString());
                    return num11 == num12;
                default:
                    return false;
            }
        }

        public static bool IsValidUPC(string UPC)
        {
            if (UPC != new Regex("[^0-9]").Replace(UPC, ""))
                return false;
            switch (UPC.Length)
            {
                case 8:
                case 12:
                case 13:
                case 14:
                    UPC = UPC.PadLeft(14, '0');
                    int[] numArray1 = new int[13];
                    numArray1[0] = int.Parse(UPC[0].ToString()) * 3;
                    numArray1[1] = int.Parse(UPC[1].ToString());
                    numArray1[2] = int.Parse(UPC[2].ToString()) * 3;
                    int[] numArray2 = numArray1;
                    char ch = UPC[3];
                    int num1 = int.Parse(ch.ToString());
                    numArray2[3] = num1;
                    int[] numArray3 = numArray1;
                    ch = UPC[4];
                    int num2 = int.Parse(ch.ToString()) * 3;
                    numArray3[4] = num2;
                    int[] numArray4 = numArray1;
                    ch = UPC[5];
                    int num3 = int.Parse(ch.ToString());
                    numArray4[5] = num3;
                    int[] numArray5 = numArray1;
                    ch = UPC[6];
                    int num4 = int.Parse(ch.ToString()) * 3;
                    numArray5[6] = num4;
                    int[] numArray6 = numArray1;
                    ch = UPC[7];
                    int num5 = int.Parse(ch.ToString());
                    numArray6[7] = num5;
                    int[] numArray7 = numArray1;
                    ch = UPC[8];
                    int num6 = int.Parse(ch.ToString()) * 3;
                    numArray7[8] = num6;
                    int[] numArray8 = numArray1;
                    ch = UPC[9];
                    int num7 = int.Parse(ch.ToString());
                    numArray8[9] = num7;
                    int[] numArray9 = numArray1;
                    ch = UPC[10];
                    int num8 = int.Parse(ch.ToString()) * 3;
                    numArray9[10] = num8;
                    int[] numArray10 = numArray1;
                    ch = UPC[11];
                    int num9 = int.Parse(ch.ToString());
                    numArray10[11] = num9;
                    int[] numArray11 = numArray1;
                    ch = UPC[12];
                    int num10 = int.Parse(ch.ToString()) * 3;
                    numArray11[12] = num10;
                    int num11 = (10 - (numArray1[0] + numArray1[1] + numArray1[2] + numArray1[3] + numArray1[4] + numArray1[5] + numArray1[6] + numArray1[7] + numArray1[8] + numArray1[9] + numArray1[10] + numArray1[11] + numArray1[12]) % 10) % 10;
                    ch = UPC[13];
                    int num12 = int.Parse(ch.ToString());
                    return num11 == num12;
                default:
                    return false;
            }
        }

        private static void SetLengthsFromPartition(ref Sgtin96 Sgtin96ToUpdate)
        {
            switch (Sgtin96ToUpdate._partition)
            {
                case 0:
                    Sgtin96ToUpdate._companyPrefixLengthInBits = 40;
                    break;
                case 1:
                    Sgtin96ToUpdate._companyPrefixLengthInBits = 37;
                    break;
                case 2:
                    Sgtin96ToUpdate._companyPrefixLengthInBits = 34;
                    break;
                case 3:
                    Sgtin96ToUpdate._companyPrefixLengthInBits = 30;
                    break;
                case 4:
                    Sgtin96ToUpdate._companyPrefixLengthInBits = 27;
                    break;
                case 5:
                    Sgtin96ToUpdate._companyPrefixLengthInBits = 24;
                    break;
                case 6:
                    Sgtin96ToUpdate._companyPrefixLengthInBits = 20;
                    break;
            }
            Sgtin96ToUpdate._itemReferenceLengthInBits = 44 - Sgtin96ToUpdate._companyPrefixLengthInBits;
            Sgtin96ToUpdate._itemReferenceLengthInDigits = 1 + Sgtin96ToUpdate._partition;
            Sgtin96ToUpdate._companyPrefixLengthInDigits = 13 - Sgtin96ToUpdate._itemReferenceLengthInDigits;
        }
    }
}
