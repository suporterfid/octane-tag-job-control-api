using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace OctaneTagWritingTest.Helpers
{
    internal class SerialGenerator
    {
        private readonly ConcurrentDictionary<string, bool> usedSerials;
        private readonly RandomNumberGenerator rng;
        private const int SERIAL_LENGTH = 10;
        private readonly object lockObject = new object();

        public SerialGenerator()
        {
            usedSerials = new ConcurrentDictionary<string, bool>();
            rng = RandomNumberGenerator.Create();
        }

        public string GenerateUniqueSerial()
        {
            string serial;
            int attempts = 0;
            const int maxAttempts = 100; // Prevent infinite loop

            lock (lockObject)
            {
                do
                {
                    serial = GenerateSerial();
                    attempts++;

                    if (attempts >= maxAttempts)
                    {
                        throw new InvalidOperationException("Unable to generate unique serial after maximum attempts");
                    }
                }
                while (!usedSerials.TryAdd(serial, true));
            }

            return serial;
        }

        private string GenerateSerial()
        {
            // For 10 hex digits, we need 5 bytes (40 bits) as each byte gives us 2 hex digits
            byte[] randomBytes = new byte[5];
            rng.GetBytes(randomBytes);

            // Convert bytes to hex string
            string hexString = BitConverter.ToString(randomBytes).Replace("-", "");

            // Ensure exactly 10 digits by taking first 10 if longer
            return hexString.Substring(0, SERIAL_LENGTH);
        }

        public bool IsSerialUsed(string serial)
        {
            return usedSerials.ContainsKey(serial);
        }

        public void Clear()
        {
            usedSerials.Clear();
        }

        ~SerialGenerator()
        {
            if (rng != null)
            {
                rng.Dispose();
            }
        }
    }
}
