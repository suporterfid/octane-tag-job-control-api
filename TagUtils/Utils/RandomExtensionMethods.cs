using System;

namespace Impinj.Utils
{
    public static class RandomExtensionMethods
    {
        public static long NextLong(this Random random, long min, long max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "max must be > min!");
            ulong num = (ulong)(max - min);
            ulong int64;
            do
            {
                byte[] buffer = new byte[8];
                random.NextBytes(buffer);
                int64 = (ulong)BitConverter.ToInt64(buffer, 0);
            }
            while (int64 > ulong.MaxValue - (ulong.MaxValue % num + 1UL) % num);
            return (long)(int64 % num) + min;
        }

        public static long NextLong(this Random random, long max) => random.NextLong(0L, max);

        public static long NextLong(this Random random) => random.NextLong(long.MinValue, long.MaxValue);
    }
}
