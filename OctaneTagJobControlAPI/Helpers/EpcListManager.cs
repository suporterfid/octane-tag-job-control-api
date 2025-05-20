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
    /// Creates a new EPC string using the provided parameters and encoding method.
    /// </summary>
    /// <param name="currentEpc">The current EPC string (must be 24 characters).</param>
    /// <param name="tid">The TID string to associate with the new EPC.</param>
    /// <param name="gtin">The GTIN to use for EPC generation.</param>
    /// <param name="companyPrefixLength">Length of the company prefix (default is 6).</param>
    /// <param name="encodingMethod">The EPC encoding method to use (default is BasicWithTidSuffix).</param>
    /// <returns>A new EPC string generated according to the specified parameters.</returns>
    public string CreateEpcWithCurrentDigits(string currentEpc, string tid, string gtin, int companyPrefixLength = 6, EpcEncodingMethod encodingMethod = EpcEncodingMethod.BasicWithTidSuffix)
    {
        if (string.IsNullOrEmpty(currentEpc) || currentEpc.Length != 24)
        //    throw new ArgumentException("Current EPC must be a 24-character string.", nameof(currentEpc));

        if (string.IsNullOrEmpty(tid))
            throw new ArgumentException("TID cannot be null or empty.", nameof(tid));

        if (string.IsNullOrEmpty(gtin))
            throw new ArgumentException("GTIN cannot be null or empty.", nameof(gtin));

        lock (lockObj)
        {
            // Use the GenerateEpc method to create the EPC with all parameters
            string newEpc = GenerateEpc(tid, gtin, companyPrefixLength, encodingMethod);

            // Store the new EPC in the dictionary associated with the TID
            generatedEpcsByTid.AddOrUpdate(tid, newEpc, (key, oldValue) => newEpc);

            Console.WriteLine($"Created new EPC {newEpc} for TID {tid} using GTIN {gtin} with encoding {encodingMethod}");
            return newEpc;
        }
    }

    /// <summary>
    /// Gets the first 14 digits of a new EPC created using CreateEpcWithCurrentDigits.
    /// This represents the configured header and item code portion of the EPC.
    /// </summary>
    /// <param name="currentEpc">The current EPC string to use for creating the new EPC.</param>
    /// <param name="tid">The TID string to associate with the new EPC.</param>
    /// <param name="gtin">The GTIN to use for EPC generation (optional, uses _epcPlainItemCode if not provided).</param>
    /// <param name="companyPrefixLength">Length of the company prefix (default is 6).</param>
    /// <param name="encodingMethod">The EPC encoding method to use (default is BasicWithTidSuffix).</param>
    /// <returns>The first 14 digits of the newly created EPC.</returns>
    public string CreateAndStoreNewEpcBasedOnCurrentPrefix(string currentEpc, string tid, string gtin = null, int companyPrefixLength = 6, EpcEncodingMethod encodingMethod = EpcEncodingMethod.BasicWithTidSuffix)
    {
        // If no GTIN provided, use the configured _epcPlainItemCode
        gtin = gtin ?? _epcPlainItemCode;
        string newEpc = CreateEpcWithCurrentDigits(currentEpc, tid, gtin, companyPrefixLength, encodingMethod);
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
    /// Initializes EPC data with the specified parameters for EPC generation.
    /// </summary>
    /// <param name="header">The EPC header.</param>
    /// <param name="code">The plain item code.</param>
    /// <param name="epcQuantity">The number of EPCs to generate (default is 1).</param>
    /// <param name="companyPrefixLength">Length of the company prefix (default is 6).</param>
    /// <param name="encodingMethod">The EPC encoding method to use (default is BasicWithTidSuffix).</param>
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

    /// <summary>
    /// Generates an EPC string using the specified parameters and encoding method.
    /// </summary>
    /// <param name="tid">The TID string to use for EPC generation.</param>
    /// <param name="gtin">The GTIN to use for EPC generation.</param>
    /// <param name="companyPrefixLength">Length of the company prefix (default is 6).</param>
    /// <param name="encodingMethod">The EPC encoding method to use (default is BasicWithTidSuffix).</param>
    /// <returns>A new EPC string generated according to the specified parameters.</returns>
    public string GenerateEpc(string tid, string gtin, int companyPrefixLength = 6, EpcEncodingMethod encodingMethod = EpcEncodingMethod.BasicWithTidSuffix)
    {
        if (string.IsNullOrEmpty(_epcPlainItemCode))
        {
            _epcPlainItemCode = gtin;
        }

        // Ensure GTIN is 14 digits
        if (gtin.Length < 14)
        {
            gtin = gtin.PadLeft(14, '0');
        }
        else if (gtin.Length > 14)
        {
            gtin = gtin.Substring(gtin.Length - 14);
        }

        switch (encodingMethod)
        {
            case EpcEncodingMethod.SGTIN96:
                try
                {
                    if (!gtin.Equals(_epcPlainItemCode, StringComparison.OrdinalIgnoreCase))
                    {
                        _epcPlainItemCode = gtin;
                        // Use the first 13 digits for SGTIN-96 encoding (excluding check digit)
                        var sgtin96 = Sgtin96.FromGTIN(gtin.Substring(0, 13), companyPrefixLength);
                        string fullEpc = sgtin96.ToEpc();
                        
                        // Take the last 10 digits from TID
                        string tidSuffix = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
                        
                        // Combine to form 24-character EPC
                        string newEpc = fullEpc.Substring(0, 14) + tidSuffix;
                        
                        Console.WriteLine($"Generated SGTIN-96 EPC for TID {tid} using GTIN {gtin}: {newEpc}");
                        return newEpc;
                    }
                    else
                    {
                        // Use existing base EPC if GTIN hasn't changed
                        string tidSuffix = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
                        string newEpc = _baseEpcHex + tidSuffix;
                        Console.WriteLine($"Generated SGTIN-96 EPC for TID {tid} using cached base: {newEpc}");
                        return newEpc;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error generating SGTIN-96 EPC: {ex.Message}. Using direct GTIN + TID encoding.");
                    // Direct encoding: Convert GTIN to hex and append TID suffix
                    string gtinHex = Convert.ToInt64(gtin).ToString("X14");
                    string tidSuffix = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
                    string newEpc = gtinHex + tidSuffix;
                    return newEpc;
                }

            case EpcEncodingMethod.CustomFormat:
            case EpcEncodingMethod.BasicWithTidSuffix:
            default:
                // Direct encoding: Convert GTIN to hex and append TID suffix
                string baseHex = Convert.ToInt64(gtin).ToString("X14");
                string suffix = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
                string epc = baseHex + suffix;
                Console.WriteLine($"Generated basic EPC for TID {tid} using GTIN {gtin}: {epc}");
                return epc;
        }
    }

    /// <summary>
    /// Generates a basic EPC by appending a TID suffix to a partial hex EPC.
    /// </summary>
    /// <param name="partialHexEpc">The partial hex EPC to use as a prefix.</param>
    /// <param name="tid">The TID string to use for the suffix (takes last 10 characters or pads if shorter).</param>
    /// <returns>A complete EPC string with the TID suffix.</returns>
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
            string createdEpcToApply = GenerateEpc(tid, _epcPlainItemCode, _companyPrefixLength, _encodingMethod);

            // If the generated EPC already exists, generate a new EPC with the next serial number.
            if (TagOpController.Instance.GetExistingEpc(createdEpcToApply))
            {
                createdEpcToApply = GenerateEpc(tid, _epcPlainItemCode, _companyPrefixLength, _encodingMethod);
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
