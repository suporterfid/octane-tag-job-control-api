using EpcListGenerator;
using Impinj.OctaneSdk;
using Impinj.TagUtils;
using OctaneTagJobControlAPI.Strategies.Base;
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
    private string _epcHeader = "B200";
    private string _epcPlainItemCode = "99999999999999";
    private long _quantity = 1;
    private int _companyPrefixLength;

    // Encoding configuration
    private EpcEncodingMethod _encodingMethod;
    private int _partitionValue;
    private int _companyPrefix;
    private int _itemReference;
    private string _baseEpcHex = null;

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
            string prefix = _epcHeader + _epcPlainItemCode;
            if (prefix.Length != 14)
                throw new InvalidOperationException("Combined header and item code must be 14 characters.");

            // Take the remaining 10 digits from the current EPC
            string remainingDigits = currentEpc.Substring(14);

            // Take the last 10 digits from the TID
            string tidSuffix = tid.Substring(14);
            tidSuffix = tidSuffix.PadLeft(10, '0');

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
    public void InitEpcData(string header, string code, long epcQuantity = 1, int companyPrefixLength = 6, EpcEncodingMethod encodingMethod = EpcEncodingMethod.BasicWithTidSuffix)
    {
        _epcHeader = header;
        _epcPlainItemCode = code;
        _quantity = epcQuantity;
        _companyPrefixLength = companyPrefixLength;
        _encodingMethod = encodingMethod;

    }

    /// <summary>
    /// Gets the next unique EPC.
    /// If a TID is provided, uses a thread-safe dictionary to ensure uniqueness.
    /// If the TID is null or empty, the current generation logic is used.
    /// </summary>
    /// <param name="tid">The tag TID used as a key for uniqueness (optional).</param>
    /// <returns>The next unique EPC string.</returns>
    public string GetNextEpc(string tid)
    {
        if (!string.IsNullOrEmpty(tid))
        {
            // If TID is provided, use the dictionary to guarantee uniqueness.
            // If an EPC for this TID hasn't been generated, GenerateUniqueEpc is called and the value is added.
            return generatedEpcsByTid.GetOrAdd(tid, key => GenerateUniqueEpc(tid));
        }
        else
        {
            // If TID is null or empty, use the current logic.
            return GenerateUniqueEpc(tid);
        }
    }

    public string GenerateEpc(string tid, string gtin, int companyPrefixLength = 6, EpcEncodingMethod encodingMethod = EpcEncodingMethod.BasicWithTidSuffix)
    {
        if (string.IsNullOrEmpty(_epcPlainItemCode))
        {
            _epcPlainItemCode = gtin;
        }

        switch (encodingMethod)
        {
            case EpcEncodingMethod.SGTIN96:
                try
                {
                    if (gtin.Length < 13)
                    {
                        gtin = gtin.PadLeft(13, '0');
                    }

                    if (!gtin.Equals(_epcPlainItemCode, StringComparison.OrdinalIgnoreCase))
                    {
                        _epcPlainItemCode = gtin;
                        var sgtin96 = Sgtin96.FromGTIN(gtin, companyPrefixLength);
                        _baseEpcHex = sgtin96.ToEpc().Substring(0, 14);
                    }


                    string serialStr = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid;

                    //if (ulong.TryParse(serialStr, System.Globalization.NumberStyles.HexNumber, null, out ulong serialNumber))
                    //{
                    //    serialNumber = Math.Min(serialNumber, 274877906943);
                    //}
                    //else
                    //{
                    //    serialNumber = (ulong)Math.Abs(tid.GetHashCode()) % 274877906943;
                    //}

                    //sgtin96.SerialNumber = serialNumber;
                    //string newEpc = sgtin96.ToEpc();
                    string newEpc = GenerateBasicEpcWithTidSuffix(_baseEpcHex, tid);

                    Console.WriteLine($"Generated SGTIN-96 EPC for TID {tid}: {newEpc}");
                    return newEpc;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error generating SGTIN-96 EPC: {ex.Message}. Falling back to basic encoding.");
                    return GenerateBasicEpcWithTidSuffix(_baseEpcHex, tid);
                }

            case EpcEncodingMethod.CustomFormat:
                Console.WriteLine("CustomFormat encoding not yet implemented, falling back to basic encoding.");
                return GenerateBasicEpcWithTidSuffix(_baseEpcHex, tid);

            case EpcEncodingMethod.BasicWithTidSuffix:
            default:
                return GenerateBasicEpcWithTidSuffix(_baseEpcHex, tid);
        }
    }

    public string GenerateBasicEpcWithTidSuffix(string partialHexEpc, string tid)
    {
        string tidSuffix = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
        string newEpc = partialHexEpc + tidSuffix;
        Console.WriteLine($"Generated basic EPC for TID {tid}: {newEpc}");
        return newEpc;
    }

    /// <summary>
    /// Generates a unique EPC based on the current serial number.
    /// Ensures thread safety during EPC generation.
    /// </summary>
    /// <returns>A unique EPC string.</returns>
    private string GenerateUniqueEpc(string tid)
    {
        lock (lockObj)
        {
            // Generate the EPC list using the current serial number.
            string createdEpcToApply =  GenerateEpc(tid, _epcPlainItemCode, _companyPrefixLength, _encodingMethod);
            //string createdEpcToApply = EpcListGeneratorHelper.Instance.GenerateEpcFromTid(
            //    tid, _epcHeader, _epcPlainItemCode);

            // If the generated EPC already exists, generate a new EPC with the next serial number.
            if (TagOpController.Instance.GetExistingEpc(createdEpcToApply))
            {
                createdEpcToApply = GenerateEpc(tid, _epcPlainItemCode, _companyPrefixLength, _encodingMethod);
                //createdEpcToApply = EpcListGeneratorHelper.Instance.GenerateEpcFromTid(
                //    tid, _epcHeader, _epcPlainItemCode);
            }

            Console.WriteLine($"Returning next EPC created: {createdEpcToApply}: SN = {currentSerialNumber}");
            return createdEpcToApply;
        }
    }

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
