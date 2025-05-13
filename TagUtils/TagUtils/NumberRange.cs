using System;
using System.Runtime.CompilerServices;

namespace Impinj.TagUtils
{
    public struct NumberRange
    {
        public long Max { get; set; }

        public long Min { get; set; }

        public NumberRange(long min, long max)
        {
            Min = min <= max ? min : throw new ArgumentOutOfRangeException(nameof(min));
            Max = max;
        }

        public NumberRange(long both) => Min = Max = both;

        public long Count => Max - Min + 1L;

        public static NumberRange FromString(string strNumberRange)
        {
            if (string.IsNullOrWhiteSpace(strNumberRange))
                return new NumberRange();
            string[] strArray = strNumberRange.Trim().Split("-".ToCharArray(), StringSplitOptions.RemoveEmptyEntries);
            if (strArray.Length < 1 || strArray.Length > 2)
            {
                DefaultInterpolatedStringHandler interpolatedStringHandler = new DefaultInterpolatedStringHandler(71, 1);
                interpolatedStringHandler.AppendFormatted(strArray.Length);
                interpolatedStringHandler.AppendLiteral(" Range values specified which is more than the 2 that were anticipated!");
                throw new InvalidCastException(interpolatedStringHandler.ToStringAndClear());
            }
            return 1 == strArray.Length ? new NumberRange(strArray[0].ToInt64()) : new NumberRange(strArray[0].ToInt64(), strArray[1].ToInt64());
        }

        public string ToHexString()
        {
            if (Min != Max)
            {
                DefaultInterpolatedStringHandler interpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 2);
                interpolatedStringHandler.AppendLiteral("0x");
                interpolatedStringHandler.AppendFormatted(Min, "X2");
                interpolatedStringHandler.AppendLiteral("-0x");
                interpolatedStringHandler.AppendFormatted(Max, "X2");
                return interpolatedStringHandler.ToStringAndClear();
            }
            DefaultInterpolatedStringHandler interpolatedStringHandler1 = new DefaultInterpolatedStringHandler(2, 1);
            interpolatedStringHandler1.AppendLiteral("0x");
            interpolatedStringHandler1.AppendFormatted(Min, "X2");
            return interpolatedStringHandler1.ToStringAndClear();
        }

        public override string ToString()
        {
            if (Min != Max)
            {
                DefaultInterpolatedStringHandler interpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 2);
                interpolatedStringHandler.AppendFormatted(Min);
                interpolatedStringHandler.AppendLiteral("-");
                interpolatedStringHandler.AppendFormatted(Max);
                return interpolatedStringHandler.ToStringAndClear();
            }
            DefaultInterpolatedStringHandler interpolatedStringHandler1 = new DefaultInterpolatedStringHandler(0, 1);
            interpolatedStringHandler1.AppendFormatted(Min);
            return interpolatedStringHandler1.ToStringAndClear();
        }
    }
}
