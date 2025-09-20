using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Impinj.TagUtils;
using OctaneTagWritingTest.Helpers;

namespace EpcListGenerator
{
    /// <summary>
    /// Singleton class that provides helper methods for generating EPC lists.
    /// </summary>
    public sealed class EpcListGeneratorHelper
    {
        // Lazy initialization of the singleton instance (thread-safe).
        private static readonly Lazy<EpcListGeneratorHelper> instance =
            new Lazy<EpcListGeneratorHelper>(() => new EpcListGeneratorHelper());

        /// <summary>
        /// Gets the singleton instance of the EpcListGeneratorHelper.
        /// </summary>
        public static EpcListGeneratorHelper Instance => instance.Value;

        // Private lock object for synchronizing operations.
        private readonly object lockObj = new object();

        // Private constructor to prevent instantiation from outside.
        private EpcListGeneratorHelper()
        {
        }

        /// <summary>
        /// Generates a list of EPC strings based on the provided GTIN and quantity.
        /// </summary>
        /// <param name="gtin">The GTIN string. If null or empty, a default value is used.</param>
        /// <param name="quantity">The number of EPCs to generate. Must be less than 10^10.</param>
        /// <returns>A list of EPC strings.</returns>
        /// <exception cref="ArgumentException">Thrown when the quantity is too high.</exception>
        public List<string> GenerateEncodedEpcList(string gtin, long quantity)
        {
            // Use default GTIN if none is provided.
            if (string.IsNullOrWhiteSpace(gtin))
            {
                gtin = "07891033079360";
            }

            // Validate the quantity.
            if (quantity >= 10000000000)
            {
                throw new ArgumentException(
                    "Quantity too high: the serial number should have up to 10 digits. Provide a quantity less than 10^10.",
                    nameof(quantity));
            }

            var epcList = new List<string>();

            // Generate EPCs sequentially.
            for (long i = 1; i <= quantity; i++)
            {
                // Create the Sgtin96 object using the provided GTIN and a partition value of 6.
                var sgtin96 = Sgtin96.FromGTIN(gtin, 6);
                sgtin96.SerialNumber = (ulong)i;
                epcList.Add(sgtin96.ToEpc());
            }

            return epcList;
        }

        /// <summary>
        /// Generates a custom list of EPC strings based on a 4-character header, a 14-character middle part, and a quantity.
        /// The resulting EPCs are 24 characters long, where the last 6 digits form a unique serial number.
        /// </summary>
        /// <param name="epcHeader">A 4-character string representing the EPC header (e.g., "B071").</param>
        /// <param name="middlePart">A 14-character string that forms the middle part of the EPC.</param>
        /// <param name="quantity">The number of EPCs to generate. Must be less than or equal to 999999.</param>
        /// <param name="initSerial">The initial serial number to start generating from.</param>
        /// <returns>A list of 24-character EPC strings.</returns>
        /// <exception cref="ArgumentException">
        /// Thrown when epcHeader is not 4 characters,
        /// or middlePart is not 14 characters,
        /// or quantity is greater than 999999.
        /// </exception>
        public List<string> GenerateCustomEpcList(string epcHeader, string middlePart, long quantity, long initSerial)
        {
            var epcList = new List<string>();
            lock (lockObj)
            {
                // Validate that epcHeader is exactly 4 characters.
                if (string.IsNullOrWhiteSpace(epcHeader) || epcHeader.Length != 4)
                {
                    throw new ArgumentException("EPC header must be exactly 4 characters long.", nameof(epcHeader));
                }

                // Validate that middlePart is exactly 14 characters.
                if (string.IsNullOrWhiteSpace(middlePart) || middlePart.Length != 14)
                {
                    throw new ArgumentException("The middle part must be exactly 14 characters long.", nameof(middlePart));
                }

                // Validate that quantity does not exceed the maximum unique serial numbers available (6 digits).
                if (quantity > 999999)
                {
                    throw new ArgumentException("Quantity too high: the unique serial number must be 6 digits. Provide a quantity of 999999 or less.", nameof(quantity));
                }

                // Generate EPCs sequentially.
                for (long i = 1; i <= quantity; i++)
                {
                    // Generate a unique 6-digit serial number with leading zeros.
                    string serial = initSerial.ToString("D6");
                    string epc = epcHeader + middlePart + serial;

                    // Ensure the resulting EPC is 24 characters long.
                    if (epc.Length != 24)
                    {
                        throw new InvalidOperationException("The generated EPC must be 24 characters long.");
                    }

                    epcList.Add(epc);
                    initSerial = initSerial + 1;
                }
            }

            return epcList;
        }
		
		 /// <summary>
		/// Generates a single EPC string from a given TID (hexadecimal string) by extracting the unique serial
		/// portion from the TID after skipping the first 2 words.
		/// 
		/// This method expects:
		///   - The EPC header to be exactly 4 characters.
		///   - The middle part to be exactly 4 characters.
		/// 
		/// Given that the final EPC must be 24 characters long, the extracted serial portion will occupy
		/// 24 - (4 + 4) = 16 characters.
		/// 
		/// The TID is expected to be in hexadecimal, with each 16-bit word represented by 4 characters.
		/// For a typical 6-word TID (24 hex characters), the first 2 words (8 hex characters) are skipped,
		/// and the remaining 4 words (16 hex characters) form the unique serial.
		/// 
		/// If the remaining portion is shorter than 16 characters, it is left-padded with zeros.
		/// If it is longer, only the rightmost 16 characters are used.
		/// 
		/// The final EPC is formed as: EPC = epcHeader (4 chars) + middlePart (4 chars) + serial (16 chars)
		/// which yields a 24-character EPC.
		/// 
		/// For example, given TID "E2801190200072B8D8830332":
		///   - TID words: "E280", "1190", "2000", "72B8", "D883", "0332"
		///   - Skipping the first two words leaves "200072B8D8830332" (16 characters),
		///   - And if epcHeader = "B071" and middlePart = "ABCD", then the EPC is "B071ABCD200072B8D8830332".
		///   (Note: In this example the final EPC is 4 + 4 + 16 = 24 characters.)
		/// </summary>
		/// <param name="tidHexString">The full TID memory as a hexadecimal string (each word is 4 hex digits).</param>
		/// <param name="epcHeader">A 4-character string representing the EPC header.</param>
		/// <param name="middlePart">A 4-character string representing the EPC middle part.</param>
		/// <returns>A single EPC string of 24 characters.</returns>
		/// <exception cref="ArgumentException">Thrown when inputs are invalid.</exception>
		public string GenerateEpcFromTid(string tidHexString, string epcHeader, string middlePart)
		{
			if (string.IsNullOrWhiteSpace(tidHexString))
				throw new ArgumentException("TID string cannot be null or empty.", nameof(tidHexString));
			tidHexString = tidHexString.Replace(" ", "").Trim().ToUpper();
			if (tidHexString.Length % 4 != 0)
				throw new ArgumentException("TID string length must be a multiple of 4.", nameof(tidHexString));

			// Convert the TID string into an array of 16-bit words.
			int totalWords = tidHexString.Length / 4;
			ushort[] tidWords = new ushort[totalWords];
			for (int i = 0; i < totalWords; i++)
			{
				string wordHex = tidHexString.Substring(i * 4, 4);
				tidWords[i] = Convert.ToUInt16(wordHex, 16);
			}

			if (totalWords <= 2)
				throw new ArgumentException("TID does not contain enough words to extract a serial.", nameof(tidHexString));

            string serialHex = tidHexString.Substring(14);

            using (var parser = new TagTidParser(tidHexString))
            {
                serialHex = parser.Get40BitSerialHex();
                Console.WriteLine($"Serial extraído: [[[[[[[[[[[[[[[[[[[{serialHex}]]]]]]]]]]]]]]]]]]]");
            }

			// Construct the final EPC.
			string epc = epcHeader + middlePart + serialHex;
			if (epc.Length != 24)
				throw new InvalidOperationException("The generated EPC must be 24 characters long.");

			return epc;
		}

        /// <summary>
        /// Saves the EPC list to a file.
        /// </summary>
        /// <param name="epcList">The list of EPC strings to save.</param>
        /// <param name="outputFilePath">The path of the output file.</param>
        public void SaveEpcListToFile(List<string> epcList, string outputFilePath)
        {
            File.WriteAllLines(outputFilePath, epcList);
        }
    }
}
