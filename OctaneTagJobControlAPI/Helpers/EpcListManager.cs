using EpcListGenerator;
using OctaneTagWritingTest.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class EpcListManager
{
    // Singleton instance with lazy initialization (thread-safe)
    private static readonly Lazy<EpcListManager> instance =
        new Lazy<EpcListManager>(() => new EpcListManager());

    /// <summary>
    /// Gets the singleton instance of the EpcListManager.
    /// </summary>
    public static EpcListManager Instance => instance.Value;

    // Instance fields
    private Queue<string> epcQueue = new Queue<string>();
    private readonly object lockObj = new object();
    private string lastEpc = "000000000000000000000000";
    private long currentSerialNumber = 1;
    private string epcHeader = "B200";
    private string epcPlainItemCode = "99999999999999";
    private long quantity = 1;

    // Thread-safe dictionary to ensure unique EPC generation using tag TID as key.
    private ConcurrentDictionary<string, string> generatedEpcsByTid = new ConcurrentDictionary<string, string>();

    // Private constructor to prevent external instantiation.
    private EpcListManager()
    {
    }

    /// <summary>
    /// Creates a new EPC string using the configured header and item code for the first 14 digits
    /// and taking the remaining 20 digits from the provided current EPC.
    /// </summary>
    /// <param name="currentEpc">The current EPC string to take the remaining digits from.</param>
    /// <param name="tid">The TID string to associate with the new EPC.</param>
    /// <returns>A new EPC string with the configured prefix and remaining digits from current EPC.</returns>
    public string CreateEpcWithCurrentDigits(string currentEpc, string tid)
    {
        if (string.IsNullOrEmpty(currentEpc) || currentEpc.Length != 24)
            throw new ArgumentException("Current EPC must be a 24-character string.", nameof(currentEpc));

        if (string.IsNullOrEmpty(tid))
            throw new ArgumentException("TID cannot be null or empty.", nameof(tid));

        lock (lockObj)
        {
            // Take the first 14 digits from configured header and item code
            string prefix = epcHeader + epcPlainItemCode;
            prefix = prefix.Trim();
            
            if (prefix.Length < 14)
                prefix = prefix.PadRight(14, '0');

            if (prefix.Length > 14)
                prefix = prefix.Substring(0, 14);
               // throw new InvalidOperationException("Combined header and item code must be 14 characters.");

            // Take the remaining 10 digits from the current EPC
            string remainingDigits = currentEpc.Substring(14);

            // Take the last 10 digits from the TID
            string tidSuffix = tid.Substring(14);
            tidSuffix = tidSuffix.PadLeft(10, '0');

            using (var parser = new TagTidParser(tid))
            {
                tidSuffix = parser.Get40BitSerialHex();
                Console.WriteLine($"Serial extraído: {tidSuffix}");
            }

            // Combine to create the new EPC
            string newEpc = prefix + tidSuffix;

            // Store the new EPC in the dictionary associated with the TID
            generatedEpcsByTid.AddOrUpdate(tid, newEpc, (key, oldValue) => newEpc);

            Console.WriteLine($"Created new EPC {newEpc} for TID {tid} using current EPC {currentEpc}");
            return newEpc;
        }
    }

    /// <summary>
    /// Gets the first 14 digits of a new EPC created using CreateEpcWithCurrentDigits.
    /// This represents the configured header and item code portion of the EPC.
    /// </summary>
    /// <param name="currentEpc">The current EPC string to use for creating the new EPC.</param>
    /// <param name="tid">The TID string to associate with the new EPC.</param>
    /// <returns>The first 14 digits of the newly created EPC.</returns>
    public string CreateAndStoreNewEpcBasedOnCurrentPrefix(string currentEpc, string tid)
    {
        string newEpc = CreateEpcWithCurrentDigits(currentEpc, tid);
        return newEpc.Substring(0, 14);
    }

    /// <summary>
    /// Loads the EPC list from the specified file.
    /// </summary>
    /// <param name="filePath">The path to the EPC file.</param>
    /// <exception cref="FileNotFoundException">Thrown if the EPC file is not found.</exception>
    public void LoadEpcList(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("EPC file not found.", filePath);

        lock (lockObj)
        {
            epcQueue.Clear();
            string[] lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                string epc = line.Trim();
                if (!string.IsNullOrEmpty(epc))
                    epcQueue.Enqueue(epc);
            }

            if (epcQueue.Any())
                lastEpc = epcQueue.Last();
        }
    }

    /// <summary>
    /// Initializes EPC data with the specified header, code, and quantity.
    /// </summary>
    /// <param name="header">The EPC header.</param>
    /// <param name="code">The plain item code.</param>
    /// <param name="epcQuantity">The number of EPCs to generate (default is 1).</param>
    public void InitEpcData(string header, string code, long epcQuantity = 1)
    {
        epcHeader = header;
        epcPlainItemCode = code;
        quantity = epcQuantity;
    }

    /// <summary>
    /// Gets the next unique EPC.
    /// If a TID is provided, uses a thread-safe dictionary to ensure uniqueness.
    /// If the TID is null or empty, the current generation logic is used.
    /// </summary>
    /// <param name="tid">The tag TID used as a key for uniqueness (optional).</param>
    /// <returns>The next unique EPC string.</returns>
    public string GetNextEpc(string epc, string tid)
    {
        return CreateEpcWithCurrentDigits(epc, tid);
    }

    /// <summary>
    /// Generates a unique EPC based on the current serial number.
    /// Ensures thread safety during EPC generation.
    /// </summary>
    /// <returns>A unique EPC string.</returns>
    //private string GenerateUniqueEpc(string tid)
    //{
    //    lock (lockObj)
    //    {
    //        // Generate the EPC list using the current serial number.
    //        string createdEpcToApply = EpcListGeneratorHelper.Instance.GenerateEpcFromTid(
    //            tid, epcHeader, epcPlainItemCode);

    //        // If the generated EPC already exists, generate a new EPC with the next serial number.
    //        if (TagOpController.Instance.GetExistingEpc(createdEpcToApply))
    //        {
    //            createdEpcToApply = EpcListGeneratorHelper.Instance.GenerateEpcFromTid(
    //                tid, epcHeader, epcPlainItemCode);
    //        }

    //        Console.WriteLine($"Returning next EPC created: {createdEpcToApply}");
    //        return createdEpcToApply;
    //    }
    //}

    /// <summary>
    /// Generates a new serial number based on the last EPC.
    /// </summary>
    /// <returns>A new serial number string.</returns>
    private string GenerateNewSerialNumber(string epc)
    {
        string prefix = epc.Substring(0, lastEpc.Length - 6);
        string lastDigits = epc.Substring(lastEpc.Length - 6);

        int number = int.Parse(lastDigits, System.Globalization.NumberStyles.HexNumber);
        number++;

        return prefix + number.ToString("X4");
    }

    /// <summary>
    /// Loads the TID list from the specified file.
    /// </summary>
    /// <param name="filePath">The path to the TID list file.</param>
    /// <returns>A list of TID strings.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the TID file is not found.</exception>
    public List<string> LoadTidList(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("TID list file not found.", filePath);

        var tidList = new List<string>();

        lock (lockObj)
        {
            string[] lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                string tid = line.Trim();
                if (!string.IsNullOrEmpty(tid))
                    tidList.Add(tid);
            }
            return tidList;
        }
    }
}
