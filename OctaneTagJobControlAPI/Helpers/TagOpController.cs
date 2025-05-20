using Impinj.OctaneSdk;
using Impinj.TagUtils;
using OctaneTagJobControlAPI.Strategies.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using static Impinj.OctaneSdk.ImpinjReader;

namespace OctaneTagWritingTest.Helpers
{
    public sealed partial  class TagOpController
    {
        // Encoding configuration
        private string _gtin;
        private string _epcHeader;
        private EpcEncodingMethod _encodingMethod;
        private int _partitionValue;
        private int _companyPrefix;
        private int _itemReference;
        private string _baseEpcHex = null;

        // Dictionary: key = TID, value = expected EPC.
        private Dictionary<string, string> expectedEpcByTid = new Dictionary<string, string>();
        // Dictionaries for recording operation results.
        private ConcurrentDictionary<string, string> operationResultByTid = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, string> operationResultWithSuccessByTid = new ConcurrentDictionary<string, string>();
        /// <summary>
        /// Dictionary to cache tag lock status for previously checked tags
        /// </summary>
        private readonly ConcurrentDictionary<string, TagLockStatus> _tagLockStatusCache = new ConcurrentDictionary<string, TagLockStatus>();

        private readonly object lockObj = new object();
        private HashSet<string> processedTids = new HashSet<string>();
        private ConcurrentDictionary<string, string> addedWriteSequences = new ConcurrentDictionary<string, string>();
        private ConcurrentDictionary<string, string> addedReadSequences = new ConcurrentDictionary<string, string>();
        // Private constructor for singleton.
        private TagOpController() { }

        // Lazy singleton instance.
        private static readonly Lazy<TagOpController> _instance = new Lazy<TagOpController>(() => new TagOpController());
        public static TagOpController Instance => _instance.Value;

        // New properties to hold the current target TID and flag.
        public string LocalTargetTid { get; private set; }
        public bool IsLocalTargetTidSet { get; private set; }

        private readonly object fileLock = new object();

        // Generic Monza/regular tags (Word 4 of Reserved Memory)
        private const int KillPasswordLockBit = 31;
        private const int KillPasswordPermalockBit = 30;
        private const int AccessPasswordLockBit = 29;
        private const int AccessPasswordPermalockBit = 28;
        private const int EPCLockBit = 27;
        private const int EPCPermalockBit = 26;
        private const int UserMemoryLockBit = 25;
        private const int UserMemoryPermalockBit = 24;
        private const int KillBit = 20;

        // M7xx series (different bit positions)
        private const int M7xxKillPasswordLockBit = 29;
        private const int M7xxKillPasswordPermalockBit = 28;
        private const int M7xxKillBit = 18;

        // Monza R6 (Word 5 of Reserved Memory for some bits)
        private const int MR6EPCLockBit = 14;
        private const int MR6PermalockBit = 14;

        // TID identifiers for different tag types
        private const string MR6_TID_Identifier = "E2801160";
        private const string M730_TID_Identifier = "E2801191";
        private const string M750_TID_Identifier = "E2801190";
        private const string M770_TID_Identifier = "E28011A0";
        private const string M775_TID_Identifier = "E2C011A2";
        private const string M780_TID_Identifier = "E28011C0";
        private const string M781_TID_Identifier = "E28011C1";
        private const string M830_M850_TID_Identifier = "E28011B0";

        public enum TagType
        {
            GenericMonza = 1,
            MonzaR6 = 2,
            M7xx = 3
        }

        public enum LockState
        {
            Unlocked,
            Locked,
            Permalocked
        }

        public class TagLockStatus
        {
            public LockState KillPasswordLockState { get; set; }
            public LockState AccessPasswordLockState { get; set; }
            public LockState EpcLockState { get; set; }
            public LockState UserMemoryLockState { get; set; }
            public bool IsKilled { get; set; }
        }

    /// <summary>
    /// Determines the tag type based on its TID
    /// </summary>
    public string GetChipModelName(Tag tag)
    {
        string chipModelName = "GenericIC";
        if (tag.IsFastIdPresent)
                chipModelName = tag.ModelDetails.ModelName.ToString();

            return chipModelName;
    }
     /// <summary>
     /// Determines the tag type based on its TID
     /// </summary>
    public TagType GetChipModel(Tag tag)
    {
        string chipModel = "";
        if (tag.IsFastIdPresent)
            chipModel = tag.ModelDetails.ModelName.ToString();

        if (tag == null || tag.Tid == null)
            return TagType.GenericMonza;

        string tidHex = tag.Tid.ToHexString();
        
        if (tidHex.StartsWith(MR6_TID_Identifier))
            return TagType.MonzaR6;
        else if (tidHex.StartsWith(M730_TID_Identifier) ||
                 tidHex.StartsWith(M750_TID_Identifier) ||
                 tidHex.StartsWith(M770_TID_Identifier) ||
                 tidHex.StartsWith(M775_TID_Identifier) ||
                 tidHex.StartsWith(M780_TID_Identifier) ||
                 tidHex.StartsWith(M781_TID_Identifier) ||
                 tidHex.StartsWith(M830_M850_TID_Identifier))
            return TagType.M7xx;
        else
            return TagType.GenericMonza;
    }

        /// <summary>
        /// Checks if a tag with the given TID is locked in any way
        /// </summary>
        /// <param name="tid">TID of the tag to check</param>
        /// <returns>True if the tag has any memory bank locked or permalocked, false otherwise</returns>
        public bool IsTagLocked(string tid)
        {
            if (string.IsNullOrEmpty(tid))
                return false;

            // Check if we have cached lock status for this TID
            if (_tagLockStatusCache.TryGetValue(tid, out TagLockStatus lockStatus))
            {
                // Return true if any memory bank is locked or permalocked
                return lockStatus.KillPasswordLockState != LockState.Unlocked ||
                       lockStatus.AccessPasswordLockState != LockState.Unlocked ||
                       lockStatus.EpcLockState != LockState.Unlocked ||
                       lockStatus.UserMemoryLockState != LockState.Unlocked;
            }

            // If we don't have cached lock status, we can't determine lock state
            // You could return a default or throw an exception here
            return false;
        }

        /// <summary>
        /// Checks if a specific memory bank is locked for a tag with the given TID
        /// </summary>
        /// <param name="tid">TID of the tag to check</param>
        /// <param name="memoryBank">Memory bank to check (Kill Password, Access Password, EPC, or User Memory)</param>
        /// <returns>The lock state of the specified memory bank, or Unlocked if unknown</returns>
        public LockState GetMemoryBankLockState(string tid, MemoryBank memoryBank)
        {
            if (string.IsNullOrEmpty(tid))
                return LockState.Unlocked;

            // Check if we have cached lock status for this TID
            if (_tagLockStatusCache.TryGetValue(tid, out TagLockStatus lockStatus))
            {
                switch (memoryBank)
                {
                    case MemoryBank.Reserved:
                        // For Reserved, we'll check the access password section
                        return lockStatus.AccessPasswordLockState;
                    case MemoryBank.Epc:
                        return lockStatus.EpcLockState;
                    case MemoryBank.Tid:
                        // TID is typically permalocked by manufacturer
                        return LockState.Permalocked;
                    case MemoryBank.User:
                        return lockStatus.UserMemoryLockState;
                    default:
                        return LockState.Unlocked;
                }
            }

            // If we don't have cached lock status, we can't determine lock state
            return LockState.Unlocked;
        }

        /// <summary>
        /// Checks if the EPC memory bank is locked for a tag with the given TID
        /// </summary>
        /// <param name="tid">TID of the tag to check</param>
        /// <returns>True if the EPC memory bank is locked or permalocked, false otherwise</returns>
        public bool IsEpcLocked(string tid)
        {
            if (string.IsNullOrEmpty(tid))
                return false;

            // Check if we have cached lock status for this TID
            if (_tagLockStatusCache.TryGetValue(tid, out TagLockStatus lockStatus))
            {
                return lockStatus.EpcLockState != LockState.Unlocked;
            }

            // If we don't have cached lock status, we can't determine lock state
            return false;
        }

        /// <summary>
        /// Checks if the EPC memory bank is permalocked for a tag with the given TID
        /// </summary>
        /// <param name="tid">TID of the tag to check</param>
        /// <returns>True if the EPC memory bank is permalocked, false otherwise</returns>
        public bool IsEpcPermalocked(string tid)
        {
            if (string.IsNullOrEmpty(tid))
                return false;

            // Check if we have cached lock status for this TID
            if (_tagLockStatusCache.TryGetValue(tid, out TagLockStatus lockStatus))
            {
                return lockStatus.EpcLockState == LockState.Permalocked;
            }

            // If we don't have cached lock status, we can't determine lock state
            return false;
        }

        /// <summary>
        /// Updates the cached lock status for a tag
        /// </summary>
        /// <param name="tid">TID of the tag</param>
        /// <param name="lockStatus">Lock status information</param>
        public void UpdateTagLockStatus(string tid, TagLockStatus lockStatus)
        {
            if (!string.IsNullOrEmpty(tid) && lockStatus != null)
            {
                _tagLockStatusCache[tid] = lockStatus;
            }
        }


        /// <summary>
        /// Gets tag lock status by reading reserved memory and analyzing lock bits
        /// </summary>
        public void GetTagLockStatus(Tag tag, ImpinjReader reader, Action<TagLockStatus> callback)
        {
            if (tag == null || reader == null || !reader.IsConnected)
                return;

            string tid = tag.Tid?.ToHexString() ?? string.Empty;
            if (string.IsNullOrEmpty(tid))
            {
                callback?.Invoke(null);
                return;
            }

            // Check if we already have this in cache
            if (_tagLockStatusCache.TryGetValue(tid, out TagLockStatus cachedStatus))
            {
                callback?.Invoke(cachedStatus);
                return;
            }

            try
            {
                // Create a tag operation sequence
                TagOpSequence seq = new TagOpSequence();
                seq.TargetTag = new TargetTag();
                seq.TargetTag.MemoryBank = MemoryBank.Epc;
                seq.TargetTag.BitPointer = BitPointers.Epc;
                seq.TargetTag.Data = tag.Epc.ToHexString();

                // Create a tag read operation to read reserved memory (words 4-5)
                TagReadOp readReservedMem = new TagReadOp();
                readReservedMem.MemoryBank = MemoryBank.Reserved;
                readReservedMem.WordPointer = 4; // Reading from word 4
                readReservedMem.WordCount = 2;   // Reading 2 words (words 4 and 5)

                // Add the operation to sequence
                seq.Ops.Add(readReservedMem);

                // Create the handler that matches the TagOpCompleteHandler delegate signature
                TagOpCompleteHandler handler = null;
                handler = (ImpinjReader sender, TagOpReport report) =>
                {
                    reader.TagOpComplete -= handler;
                    reader.Stop();

                    foreach (TagOpResult result in report)
                    {
                        if (result is TagReadOpResult readResult)
                        {
                            if (readResult.Result == ReadResultStatus.Success)
                            {
                                uint lockBits = readResult.Data.ToUnsignedInt();
                                TagLockStatus lockStatus = ProcessLockBits(lockBits, GetChipModel(readResult.Tag));

                                // Update the cache with the new lock status
                                if (lockStatus != null)
                                {
                                    UpdateTagLockStatus(tid, lockStatus);
                                }

                                callback?.Invoke(lockStatus);
                                return;
                            }
                        }
                    }
                    callback?.Invoke(null);
                };

                // Register the handler and start the reader
                reader.TagOpComplete += handler;
                reader.AddOpSequence(seq);
                reader.Start();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting tag lock status: {ex.Message}");
                callback?.Invoke(null);
            }
        }

        /// <summary>
        /// Process lock bits to determine lock status of various memory banks
        /// </summary>
        private TagLockStatus ProcessLockBits(uint lockBits, TagType tagType)
    {
        TagLockStatus status = new TagLockStatus();

        // Check Kill Password lock status
        bool killPasswordLockBit = false;
        bool killPasswordPermalockBit = false;

        switch (tagType)
        {
            case TagType.GenericMonza:
            case TagType.MonzaR6:
                killPasswordLockBit = IsBitSet(lockBits, KillPasswordLockBit);
                killPasswordPermalockBit = IsBitSet(lockBits, KillPasswordPermalockBit);
                break;
            case TagType.M7xx:
                killPasswordLockBit = IsBitSet(lockBits, M7xxKillPasswordLockBit);
                killPasswordPermalockBit = IsBitSet(lockBits, M7xxKillPasswordPermalockBit);
                break;
        }

        if (killPasswordPermalockBit)
            status.KillPasswordLockState = LockState.Permalocked;
        else if (killPasswordLockBit)
            status.KillPasswordLockState = LockState.Locked;
        else
            status.KillPasswordLockState = LockState.Unlocked;

        // Check Access Password lock status
        bool accessPasswordLockBit = IsBitSet(lockBits, AccessPasswordLockBit);
        bool accessPasswordPermalockBit = IsBitSet(lockBits, AccessPasswordPermalockBit);

        if (accessPasswordPermalockBit)
            status.AccessPasswordLockState = LockState.Permalocked;
        else if (accessPasswordLockBit)
            status.AccessPasswordLockState = LockState.Locked;
        else
            status.AccessPasswordLockState = LockState.Unlocked;

        // Check EPC lock status
        bool epcLockBit = false;
        bool epcPermalockBit = false;

        switch (tagType)
        {
            case TagType.GenericMonza:
            case TagType.M7xx:
                epcLockBit = IsBitSet(lockBits, EPCLockBit);
                epcPermalockBit = IsBitSet(lockBits, EPCPermalockBit);
                break;
            case TagType.MonzaR6:
                // For Monza R6, some bits are in word 5
                epcLockBit = IsBitSet(lockBits, MR6EPCLockBit);
                epcPermalockBit = IsBitSet(lockBits, MR6PermalockBit);
                break;
        }

        if (epcPermalockBit)
            status.EpcLockState = LockState.Permalocked;
        else if (epcLockBit)
            status.EpcLockState = LockState.Locked;
        else
            status.EpcLockState = LockState.Unlocked;

        // Check User Memory lock status
        bool userMemoryLockBit = IsBitSet(lockBits, UserMemoryLockBit);
        bool userMemoryPermalockBit = IsBitSet(lockBits, UserMemoryPermalockBit);

        if (userMemoryPermalockBit)
            status.UserMemoryLockState = LockState.Permalocked;
        else if (userMemoryLockBit)
            status.UserMemoryLockState = LockState.Locked;
        else
            status.UserMemoryLockState = LockState.Unlocked;

        // Check if tag is killed
        bool killBit = false;
        switch (tagType)
        {
            case TagType.GenericMonza:
            case TagType.MonzaR6:
                killBit = IsBitSet(lockBits, KillBit);
                break;
            case TagType.M7xx:
                killBit = IsBitSet(lockBits, M7xxKillBit);
                break;
        }
        status.IsKilled = killBit;

        return status;
    }

    /// <summary>
    /// Helper method to check if a specific bit is set in a value
    /// </summary>
    private bool IsBitSet(uint value, int bitPosition)
    {
        return (value & (1u << bitPosition)) != 0;
    }


        public void CleanUp()
        {
            lock (lockObj)
            {
                try
                {
                    addedWriteSequences.Clear();
                    addedReadSequences.Clear();
                    processedTids.Clear();
                    expectedEpcByTid.Clear();
                    operationResultByTid.Clear();
                    operationResultWithSuccessByTid.Clear();
                }
                catch (Exception)
                {


                }

            }
        }

        public bool HasResult(string tid)
        {
            lock (lockObj)
            {
                return operationResultByTid.ContainsKey(tid);
            }
        }

        

        public void RecordExpectedEpc(string tid, string expectedEpc)
        {
            lock (lockObj)
            {
                if (!expectedEpcByTid.ContainsKey(tid))
                    expectedEpcByTid.Add(tid, expectedEpc);
                else
                    expectedEpcByTid[tid] = expectedEpc;
            }
        }

        public bool IsTidProcessed(string tidHex)
        {
            lock (lockObj)
            {
                return processedTids.Contains(tidHex);
            }
        }

        public string GetExpectedEpc(string tid)
        {
            lock (lockObj)
            {
                if (expectedEpcByTid.TryGetValue(tid, out string expected))
                    return expected;
                return null;
            }
        }

        public void RecordResult(string tid, string result, bool wasSuccess)
        {
            lock (lockObj)
            {
                if (!processedTids.Contains(tid))
                {
                    processedTids.Add(tid);
                }

                if (wasSuccess)
                {
                    if(!operationResultWithSuccessByTid.ContainsKey(tid))
                    {
                        operationResultWithSuccessByTid.TryAdd(tid, result);
                        Console.WriteLine($"RecordResult - Success count: TID: {tid} result: {result}");
                        Console.WriteLine($"Success count: [{operationResultWithSuccessByTid.Count()}]");
                    }
                    
                    
                }

                if (!operationResultByTid.ContainsKey(tid))
                    operationResultByTid.TryAdd(tid, result);

            }
        }

        public string GetNextEpcForTag(string epc, string tid, string gtin, int companyPrefixLength = 6, EpcEncodingMethod encodingMethod = EpcEncodingMethod.BasicWithTidSuffix)
        {
            const int maxRetries = 5;
            int retryCount = 0;
            string nextEpc;

            lock (lockObj)
            {
                do
                {
                    // Get a new EPC from the manager.
                    if(string.IsNullOrEmpty(epc))
                    {
                        nextEpc = EpcListManager.Instance.GetNextEpc(tid);
                    }
                    else
                    {
                        nextEpc = EpcListManager.Instance.CreateEpcWithCurrentDigits(epc, tid, _gtin ?? "99999999999999", companyPrefixLength, encodingMethod);
                    }
                    

                    // If the EPC does not already exist, break out of the loop.
                    if (!GetExistingEpc(nextEpc))
                    {
                        break;
                    }

                    retryCount++;
                }
                while (retryCount < maxRetries);

                // If after the maximum retries the EPC still exists, throw an exception.
                if (GetExistingEpc(nextEpc))
                {
                    Console.WriteLine("WARNING DUP_EPC: Unable to generate a unique EPC after maximum retries.");
                }

                return nextEpc;
            }
        }


        public int GetTotalReadCount()
        {
            return expectedEpcByTid.Count();
        }

        public int GetSuccessCount()
        {
            return operationResultWithSuccessByTid.Count();
        }

        public bool GetExistingEpc(string epc)
        {
            lock (lockObj)
            {
                return expectedEpcByTid.Values.Contains(epc);
            }
        }

        public void PermaLockTag(Tag tag, string accessPassword, ImpinjReader reader)
        {
            try
            {
                TagOpSequence seq = new TagOpSequence();
                // Set target tag using TID.
                seq.TargetTag.MemoryBank = MemoryBank.Tid;
                seq.TargetTag.BitPointer = 0;
                seq.TargetTag.Data = tag.Tid.ToHexString();

                // Add write operation to set the access password.
                seq.Ops.Add(new TagWriteOp
                {
                    MemoryBank = MemoryBank.Reserved,
                    WordPointer = WordPointers.AccessPassword,
                    Data = TagData.FromHexString(accessPassword)
                });

                // Create a lock operation.
                TagLockOp permalockOp = new TagLockOp
                {
                    AccessPasswordLockType = TagLockState.Permalock,
                    EpcLockType = TagLockState.Permalock,
                };

                seq.Ops.Add(permalockOp);

                try
                {
                    addedWriteSequences.TryAdd(seq.Id.ToString(), tag.Tid.ToHexString());
                }
                catch (Exception)
                {

                }

                CheckAndCleanAccessSequencesOnReader(addedWriteSequences, reader);

                reader.AddOpSequence(seq);
                Console.WriteLine($"Scheduled lock operation for TID: {tag.Tid.ToHexString()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error locking tag: {ex.Message}");
            }
        }

        public void LockTag(Tag tag, string accessPassword, ImpinjReader reader)
        {
            try
            {
                TagOpSequence seq = new TagOpSequence();
                seq.TargetTag.MemoryBank = MemoryBank.Tid;
                seq.TargetTag.BitPointer = 0;
                seq.TargetTag.Data = tag.Tid.ToHexString();

                TagLockOp lockOp = new TagLockOp
                {
                    AccessPasswordLockType = TagLockState.Lock,
                    EpcLockType = TagLockState.Lock,
                };

                seq.Ops.Add(lockOp);
                seq.Ops.Add(new TagWriteOp
                {
                    MemoryBank = MemoryBank.Reserved,
                    WordPointer = WordPointers.AccessPassword,
                    Data = TagData.FromHexString(accessPassword)
                });

                try
                {
                    addedWriteSequences.TryAdd(seq.Id.ToString(), tag.Tid.ToHexString());
                }
                catch (Exception)
                {

                }
                CheckAndCleanAccessSequencesOnReader(addedWriteSequences, reader);

                reader.AddOpSequence(seq);
                Console.WriteLine($"Scheduled lock operation for TID: {tag.Tid.ToHexString()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error locking tag: {ex.Message}");
            }
        }

        public void CheckAndCleanAccessSequencesOnReader(ConcurrentDictionary<string, string> addedSequences, ImpinjReader reader)
        {
            try
            {
                if(addedSequences.Count > 50)
                {
                    Console.WriteLine($"Cleaning-up Access Sequences {addedSequences.Count()}...");
                    reader.DeleteAllOpSequences();
                    addedSequences.Clear();
                    Console.WriteLine($" ********************* Reader Sequences cleaned-up *********************");
                }

            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning while trying to clean-up {addedSequences} sequences");
                
            }

        }
        /// <summary>
        /// Triggers a partial write operation that updates only the specified number of characters in the EPC while preserving the rest.
        /// </summary>
        /// <param name="tag">The tag to write to</param>
        /// <param name="newEpcToWrite">The new EPC value to partially write</param>
        /// <param name="reader">The RFID reader instance</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <param name="swWrite">Stopwatch for timing the write operation</param>
        /// <param name="newAccessPassword">Access password for the tag</param>
        /// <param name="encodeOrDefault">If true, uses the provided EPC; if false, uses a default pattern</param>
        /// <param name="charactersToWrite">Number of characters to write (minimum 8, default 14)</param>
        /// <param name="targetAntennaPort">Target antenna port (default 1)</param>
        /// <param name="useBlockWrite">Whether to use block write operations (default true)</param>
        /// <param name="sequenceMaxRetries">Maximum number of retries for the sequence (default 5)</param>
        public void TriggerPartialWriteAndVerify(Tag tag, string newEpcToWrite, ImpinjReader reader, CancellationToken cancellationToken, Stopwatch swWrite, string newAccessPassword, bool encodeOrDefault, int charactersToWrite = 14, ushort targetAntennaPort = 1, bool useBlockWrite = true, ushort sequenceMaxRetries = 5)
        {
            if (cancellationToken.IsCancellationRequested) return;

            // Validate minimum characters requirement (2 words = 8 characters)
            if (charactersToWrite < 8)
            {
                throw new ArgumentException("Minimum characters to write must be 8 (2 words)", nameof(charactersToWrite));
            }

            string oldEpc = tag.Epc.ToHexString();
            
            // Take only the specified number of characters from the new EPC
            string partialNewEpc = newEpcToWrite.Substring(0, Math.Min(charactersToWrite, newEpcToWrite.Length));
            
            // Keep the remaining characters from the old EPC
            string remainingOldEpc = oldEpc.Length > charactersToWrite ? oldEpc.Substring(charactersToWrite) : "";
            
            // Set EPC data based on encoding choice
            string epcData = encodeOrDefault 
                ? partialNewEpc + remainingOldEpc 
                : $"B071000000000000000000{processedTids.Count:D2}";

            string currentTid = tag.Tid.ToHexString();
            Console.WriteLine($"TriggerPartialWriteAndVerify - Attempting partial write operation for TID {currentTid}: {oldEpc} -> {epcData} (Writing first {charactersToWrite} characters) - Read RSSI {tag.PeakRssiInDbm}");
            
            TagOpSequence seq = new TagOpSequence();
            seq.SequenceStopTrigger = SequenceTriggerType.None;
            seq.TargetTag.MemoryBank = MemoryBank.Tid;
            seq.TargetTag.BitPointer = 0;
            seq.TargetTag.Data = currentTid;
            
            if (useBlockWrite)
            {
                seq.BlockWriteEnabled = true;
                seq.BlockWriteWordCount = 2; // Calculate words based on characters
                seq.BlockWriteRetryCount = 3;
            }

            if (sequenceMaxRetries > 0)
            {
                seq.SequenceStopTrigger = SequenceTriggerType.ExecutionCount;
                seq.ExecutionCount = sequenceMaxRetries;
            }
            else
            {
                seq.SequenceStopTrigger = SequenceTriggerType.None;
            }

            TagWriteOp writeOp = new TagWriteOp();
            writeOp.AccessPassword = TagData.FromHexString(newAccessPassword);
            writeOp.MemoryBank = MemoryBank.Epc;
            writeOp.WordPointer = WordPointers.Epc;
            writeOp.Data = TagData.FromHexString(epcData);

            seq.Ops.Add(writeOp);

            swWrite.Restart();

            try
            {
                addedWriteSequences.TryAdd(seq.Id.ToString(), tag.Tid.ToHexString());
            }
            catch (Exception)
            {
            }

            CheckAndCleanAccessSequencesOnReader(addedWriteSequences, reader);
            try
            {
                reader.AddOpSequence(seq);
            }
            catch (Exception)
            {
                Console.WriteLine($" *************************************************************** ");
                Console.WriteLine($" *************************************************************** ");
                Console.WriteLine($"TriggerPartialWriteAndVerify - ERROR: error while trying to add sequence {seq.Id} to TID {currentTid}");
                Console.WriteLine($" *************************************************************** ");
                Console.WriteLine($" *************************************************************** ");
                try
                {
                    Console.WriteLine($"TriggerPartialWriteAndVerify - Cleaning-up Access Sequences {addedWriteSequences.Count()}...");
                    reader.DeleteAllOpSequences();
                    addedWriteSequences.Clear();
                    reader.AddOpSequence(seq);
                    Console.WriteLine($" *************************************************************** ");
                    Console.WriteLine($" ********************* Reader Sequences cleaned-up *********************");
                    Console.WriteLine($" *************************************************************** ");
                }
                catch (Exception)
                {
                    Console.WriteLine($" *************************************************************** ");
                    Console.WriteLine($" *************************************************************** ");
                    Console.WriteLine($"TriggerPartialWriteAndVerify - Error while trying to clean-up {addedWriteSequences.Count()} sequences");
                    Console.WriteLine($" *************************************************************** ");
                    Console.WriteLine($" *************************************************************** ");
                }
            }
            
            Console.WriteLine($"TriggerPartialWriteAndVerify - Added Partial Write OpSequence {seq.Id} to TID {currentTid} - Current EPC: {oldEpc} -> Expected EPC {epcData}");

            RecordExpectedEpc(currentTid, epcData);
        }

        public void TriggerWriteAndVerify(Tag tag, string newEpcToWrite, ImpinjReader reader, CancellationToken cancellationToken, Stopwatch swWrite, string newAccessPassword, bool encodeOrDefault, ushort targetAntennaPort = 1, bool useBlockWrite = true, ushort sequenceMaxRetries = 5)
        {
            if (cancellationToken.IsCancellationRequested) return;

            string oldEpc = tag.Epc.ToHexString();
            // Set EPC data based on encoding choice.
            string epcData = encodeOrDefault ? newEpcToWrite : $"B071000000000000000000{processedTids.Count:D2}";
            string currentTid = tag.Tid.ToHexString();
            //Console.WriteLine($"Attempting robust operation for TID {currentTid}: {oldEpc} -> {newEpcToWrite} - Read RSSI {tag.PeakRssiInDbm}");
            
            TagOpSequence seq = new TagOpSequence();
            //seq.AntennaId = targetAntennaPort;
            seq.SequenceStopTrigger = SequenceTriggerType.None;
            seq.TargetTag.MemoryBank = MemoryBank.Tid;
            seq.TargetTag.BitPointer = 0;
            seq.TargetTag.Data = currentTid;
            if (useBlockWrite) // If block write is enabled, set the block write parameters.
            {
                seq.BlockWriteEnabled = true;
                seq.BlockWriteWordCount = 2;
                seq.BlockWriteRetryCount = 3;
            }

            if (sequenceMaxRetries > 0)
            {
                seq.SequenceStopTrigger = SequenceTriggerType.ExecutionCount;
                seq.ExecutionCount = sequenceMaxRetries;
            }
            else
            {
                seq.SequenceStopTrigger = SequenceTriggerType.None;
            }


            TagWriteOp writeOp = new TagWriteOp();
            writeOp.AccessPassword = TagData.FromHexString(newAccessPassword);
            writeOp.MemoryBank = MemoryBank.Epc;
            writeOp.WordPointer = WordPointers.Epc;
            writeOp.Data = TagData.FromHexString(epcData);

            Console.WriteLine($"Adding a write operation sequence to write the new EPC {epcData} to tag TID {currentTid}");
            seq.Ops.Add(writeOp);

            // If the new EPC is a different length, update the PC bits.
            if (oldEpc.Length != epcData.Length)
            {
                ushort newEpcLenWords = (ushort)(newEpcToWrite.Length / 4);
                ushort newPcBits = PcBits.AdjustPcBits(tag.PcBits, newEpcLenWords);
                Console.WriteLine("Adding a write operation to change the PC bits from :");
                Console.WriteLine("{0} to {1}\n", tag.PcBits.ToString("X4"), newPcBits.ToString("X4"));

                TagWriteOp writePc = new TagWriteOp();
                writePc.MemoryBank = MemoryBank.Epc;
                writePc.Data = TagData.FromWord(newPcBits);
                writePc.WordPointer = WordPointers.PcBits;
                seq.Ops.Add(writePc);
            }

            swWrite.Restart();

            try
            {
                addedWriteSequences.TryAdd(seq.Id.ToString(), tag.Tid.ToHexString());
            }
            catch (Exception)
            {

            }

            CheckAndCleanAccessSequencesOnReader(addedWriteSequences, reader);
            try
            {
                reader.AddOpSequence(seq);
            }
            catch (Exception)
            {
                Console.WriteLine($"TriggerWriteAndVerify - ERROR: error while trying to add sequence {seq.Id} to TID {currentTid}");
                try
                {
                    Console.WriteLine($"TriggerWriteAndVerify - Cleaning-up Access Sequences {addedWriteSequences.Count()}...");
                    reader.DeleteAllOpSequences();
                    addedWriteSequences.Clear();
                    reader.AddOpSequence(seq);
                    Console.WriteLine($" ********************* Reader Sequences cleaned-up *********************");
                }
                catch (Exception)
                {
                    Console.WriteLine($"TriggerWriteAndVerify - Error while trying to clean-up {addedWriteSequences.Count()} sequences");
                }
            }


            //Console.WriteLine($"Added Write OpSequence {seq.Id} to TID {currentTid} - Current EPC: {oldEpc} -> Expected EPC {epcData}");
            
            

            RecordExpectedEpc(currentTid, epcData);
        }

        public void TriggerVerificationRead(Tag tag, ImpinjReader reader, CancellationToken cancellationToken, Stopwatch swVerify, string newAccessPassword)
        {
            if (cancellationToken.IsCancellationRequested) return;

            string currentTid = tag.Tid.ToHexString();
            string expectedEpc = GetExpectedEpc(currentTid);

            TagOpSequence seq = new TagOpSequence();
            //seq.AntennaId = ;
            seq.SequenceStopTrigger = SequenceTriggerType.None;
            seq.TargetTag.MemoryBank = MemoryBank.Tid;
            seq.TargetTag.BitPointer = 0;
            seq.TargetTag.Data = currentTid;
            seq.BlockWriteEnabled = true;
            seq.BlockWriteWordCount = 2;
            seq.BlockWriteRetryCount = 3;

            TagReadOp readOp = new TagReadOp();
            readOp.AccessPassword = TagData.FromHexString(newAccessPassword);
            readOp.MemoryBank = MemoryBank.Epc;
            readOp.WordPointer = WordPointers.Epc;
            ushort wordCount = (ushort)(expectedEpc.Length / 4);
            readOp.WordCount = wordCount;
            seq.Ops.Add(readOp);

            try
            {
                addedReadSequences.TryAdd(seq.Id.ToString(), tag.Tid.ToHexString());
            }
            catch (Exception)
            {

            }

            CheckAndCleanAccessSequencesOnReader(addedReadSequences, reader);

            swVerify.Restart();
            try
            {
                reader.AddOpSequence(seq);
            }
            catch (Exception)
            {
                Console.WriteLine($" ###################################################### ");
                Console.WriteLine($"TriggerVerificationRead - ERROR: error while trying to add sequence {seq.Id} to TID {currentTid}");
                Console.WriteLine($" ###################################################### ");
                try
                {
                    Console.WriteLine($"TriggerVerificationRead - Cleaning-up Access Sequences {addedReadSequences.Count()}...");
                    reader.DeleteAllOpSequences();
                    addedReadSequences.Clear();
                    reader.AddOpSequence(seq);
                    Console.WriteLine($" ********************* Reader Sequences cleaned-up *********************");
                }
                catch (Exception)
                {
                    Console.WriteLine($" ###################################################### ");
                    Console.WriteLine($" ###################################################### ");
                    Console.WriteLine($" # TriggerVerificationRead - Error while trying to clean-up {addedReadSequences.Count()} sequences #");
                    Console.WriteLine($" ###################################################### ");
                    Console.WriteLine($" ###################################################### ");
                }
            }
        }

        // New helper method to process a verified tag.
        public void HandleVerifiedTag(Tag tag, string tidHex, string expectedEpc, Stopwatch swWrite, Stopwatch swVerify, ConcurrentDictionary<string, int> retryCount, Tag currentTargetTag, string chipModel, string logFile)
        {
            // Record the successful result.
            RecordResult(tidHex, tag.Epc.ToHexString(), true);

            // Update the singleton’s target tag properties.
            LocalTargetTid = tidHex;
            IsLocalTargetTidSet = true;

            Console.WriteLine($"Tag {tidHex} already has expected EPC: {tag.Epc.ToHexString()} - Success count {GetSuccessCount()}");
            swVerify.Stop();
            string verifiedEpc = tag.Epc.ToHexString() ?? "N/A";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string resultStatus = verifiedEpc.Equals(expectedEpc, StringComparison.OrdinalIgnoreCase) ? "Success" : "Failure";
            long writeTime = swWrite.ElapsedMilliseconds;
            long verifyTime = swVerify.ElapsedMilliseconds;
            int retries = retryCount.ContainsKey(tidHex) ? retryCount[tidHex] : 0;
            double resultRssi = 0;
            if (tag.IsPcBitsPresent)
                resultRssi = tag.PeakRssiInDbm;
            ushort antennaPort = 0;
            if (tag.IsAntennaPortNumberPresent)
                antennaPort = tag.AntennaPortNumber;

            // Log the CSV entry.
            string logLine = $"{timestamp},{tidHex},{currentTargetTag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{writeTime},{verifyTime},{resultStatus},{retries},{resultRssi},{antennaPort},{chipModel}";
            LogToCsv(logFile, logLine);
        }

        /// <summary>
        /// Appends a line to the CSV log file
        /// </summary>
        /// <param name="line">The line to append to the log file</param>
        public void LogToCsv(string logFile, string line)
        {
            try
            {
                lock (fileLock)
                {
                    File.AppendAllText(logFile, line + Environment.NewLine);
                }
            }
            catch (Exception)
            {
                Console.WriteLine($" +++++++++++++++++++++++++++++++++++++++++++++++++++++ ");
                Console.WriteLine($"Warning: Unable to write data to log file {logFile}");
                Console.WriteLine($" +++++++++++++++++++++++++++++++++++++++++++++++++++++ ");
            }
        }

        public void ProcessVerificationResult(TagReadOpResult readResult, string tidHex, ConcurrentDictionary<string, int> recoveryCount, Stopwatch swWrite, Stopwatch swVerify, string logFile, ImpinjReader reader, CancellationToken cancellationToken, string newAccessPassword, int maxRecoveryAttempts)
        {
            swVerify.Stop();

            string expectedEpc = GetExpectedEpc(tidHex);
            string verifiedEpc = readResult.Data?.ToHexString() ?? "N/A";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string resultStatus = verifiedEpc.Equals(expectedEpc, StringComparison.OrdinalIgnoreCase) ? "Success" : "Failure";

            int attempts = recoveryCount.GetOrAdd(tidHex, 0);

            if (resultStatus == "Failure" && attempts < maxRecoveryAttempts)
            {
                recoveryCount[tidHex] = attempts + 1;
                Console.WriteLine($"Verification failed, retry {recoveryCount[tidHex]} for TID {tidHex}");
                // Use the same method (partial or full write) that was originally used
                if (expectedEpc.Length == readResult.Tag.Epc.ToHexString().Length)
                {
                    TriggerPartialWriteAndVerify(readResult.Tag, expectedEpc, reader, cancellationToken, swWrite, newAccessPassword, true);
                }
                else
                {
                    TriggerWriteAndVerify(readResult.Tag, expectedEpc, reader, cancellationToken, swWrite, newAccessPassword, true);
                }
            }
            else
            {
                double rssi = readResult.Tag.IsPcBitsPresent ? readResult.Tag.PeakRssiInDbm : 0;
                ushort antennaPort = readResult.Tag.IsAntennaPortNumberPresent ? readResult.Tag.AntennaPortNumber : (ushort)0;

                Console.WriteLine($"Verification for TID {tidHex}: EPC read = {verifiedEpc} ({resultStatus})");

                string logLine = $"{timestamp},{tidHex},{readResult.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWrite.ElapsedMilliseconds},{swVerify.ElapsedMilliseconds},{resultStatus},{attempts},{rssi},{antennaPort}";
                LogToCsv(logFile, logLine);

                RecordResult(tidHex, resultStatus, resultStatus == "Success");
            }
        }

        /// <summary>
        /// Versão aprimorada de TriggerWriteAndVerify que também registra métricas de performance e inclui rastreamento adicional
        /// </summary>
        /// <param name="tag">O objeto Tag representando a tag RFID</param>
        /// <param name="newEpcToWrite">O novo EPC a ser gravado na tag</param>
        /// <param name="reader">O leitor RFID a ser usado para a operação</param>
        /// <param name="cancellationToken">Token para cancelar a operação</param>
        /// <param name="swWrite">O cronômetro para medir o tempo de gravação</param>
        /// <param name="newAccessPassword">A senha de acesso para a tag</param>
        /// <param name="encodeOrDefault">Se deve usar codificação específica ou valor padrão</param>
        /// <param name="targetAntennaPort">A porta da antena a ser usada</param>
        /// <param name="useBlockWrite">Se deve usar operações de escrita em bloco</param>
        /// <param name="sequenceMaxRetries">Número máximo de retentativas para a sequência</param>
        /// <returns>Um identificador de sequência para rastreamento da operação</returns>
        public string TriggerWriteAndVerifyWithMetrics(Tag tag, string newEpcToWrite, ImpinjReader reader, CancellationToken cancellationToken, Stopwatch swWrite, string newAccessPassword, bool encodeOrDefault, ushort targetAntennaPort = 1, bool useBlockWrite = true, ushort sequenceMaxRetries = 5)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            string oldEpc = tag.Epc.ToHexString();
            // Definir dados EPC com base na escolha de codificação
            string epcData = encodeOrDefault ? newEpcToWrite : $"B071000000000000000000{processedTids.Count:D2}";
            string currentTid = tag.Tid.ToHexString();

            // Registrar detalhes para diagnóstico e rastreamento
            Console.WriteLine($"TriggerWriteAndVerifyWithMetrics - Tentando operação robusta para TID {currentTid}: {oldEpc} -> {newEpcToWrite} - RSSI lido {tag.PeakRssiInDbm}");

            // Criar a sequência de operação de tag
            TagOpSequence seq = new TagOpSequence();
            seq.SequenceStopTrigger = SequenceTriggerType.None;
            seq.TargetTag.MemoryBank = MemoryBank.Tid;
            seq.TargetTag.BitPointer = 0;
            seq.TargetTag.Data = currentTid;

            // Configurar escrita em bloco se habilitada
            if (useBlockWrite)
            {
                seq.BlockWriteEnabled = true;
                seq.BlockWriteWordCount = 2;
                seq.BlockWriteRetryCount = 3;
            }

            // Configurar retentativas
            if (sequenceMaxRetries > 0)
            {
                seq.SequenceStopTrigger = SequenceTriggerType.ExecutionCount;
                seq.ExecutionCount = sequenceMaxRetries;
            }
            else
            {
                seq.SequenceStopTrigger = SequenceTriggerType.None;
            }

            // Criar operação de escrita
            TagWriteOp writeOp = new TagWriteOp();
            writeOp.AccessPassword = TagData.FromHexString(newAccessPassword);
            writeOp.MemoryBank = MemoryBank.Epc;
            writeOp.WordPointer = WordPointers.Epc;
            writeOp.Data = TagData.FromHexString(epcData);

            seq.Ops.Add(writeOp);

            // Se o novo EPC tem comprimento diferente, atualizar os bits PC
            if (oldEpc.Length != epcData.Length)
            {
                ushort newEpcLenWords = (ushort)(newEpcToWrite.Length / 4);
                ushort newPcBits = PcBits.AdjustPcBits(tag.PcBits, newEpcLenWords);
                Console.WriteLine($"Adicionando uma operação de escrita para alterar os bits PC de {tag.PcBits.ToString("X4")} para {newPcBits.ToString("X4")}");

                TagWriteOp writePc = new TagWriteOp();
                writePc.MemoryBank = MemoryBank.Epc;
                writePc.Data = TagData.FromWord(newPcBits);
                writePc.WordPointer = WordPointers.PcBits;
                seq.Ops.Add(writePc);
            }

            // Registrar o ID da sequência para rastreamento
            string sequenceId = seq.Id.ToString();

            // Iniciar o cronômetro para medir o tempo de gravação
            swWrite.Restart();

            // Registrar a sequência para rastreamento
            try
            {
                addedWriteSequences.TryAdd(sequenceId, tag.Tid.ToHexString());

                // Registrar mais métricas aqui
                RecordTagRead(); // Incrementar contagem de leituras para cálculo de taxa
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Aviso: Não foi possível rastrear a sequência de escrita: {ex.Message}");
            }

            // Limpar sequências se necessário para evitar sobrecarga
            CheckAndCleanAccessSequencesOnReader(addedWriteSequences, reader);

            // Adicionar a sequência ao leitor
            try
            {
                reader.AddOpSequence(seq);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TriggerWriteAndVerifyWithMetrics - ERRO: erro ao tentar adicionar sequência {sequenceId} para TID {currentTid}: {ex.Message}");
                try
                {
                    Console.WriteLine($"TriggerWriteAndVerifyWithMetrics - Limpando sequências de acesso {addedWriteSequences.Count()}...");
                    reader.DeleteAllOpSequences();
                    addedWriteSequences.Clear();
                    reader.AddOpSequence(seq);
                    Console.WriteLine($" ********************* Sequências do leitor limpas *********************");
                }
                catch (Exception innerEx)
                {
                    Console.WriteLine($"TriggerWriteAndVerifyWithMetrics - Erro ao tentar limpar {addedWriteSequences.Count()} sequências: {innerEx.Message}");
                    return null; // Retornar null indica falha
                }
            }

            Console.WriteLine($"TriggerWriteAndVerifyWithMetrics - Adicionada OpSequence de escrita {sequenceId} para TID {currentTid} - EPC atual: {oldEpc} -> EPC esperado {epcData}");

            // Registrar o EPC esperado para esta tag
            RecordExpectedEpc(currentTid, epcData);

            // Retornar o ID da sequência para referência
            return sequenceId;
        }

        /// <summary>
        /// Versão estendida do método HandleVerifiedTag que também registra métricas detalhadas para análise de performance
        /// </summary>
        /// <param name="tag">O objeto Tag representando a tag RFID</param>
        /// <param name="tidHex">O TID da tag em formato hexadecimal</param>
        /// <param name="expectedEpc">O EPC esperado após a gravação</param>
        /// <param name="swWrite">O cronômetro usado para medir o tempo de gravação</param>
        /// <param name="swVerify">O cronômetro usado para medir o tempo de verificação</param>
        /// <param name="cycleCount">Dicionário para rastreamento de contagem de ciclos por TID</param>
        /// <param name="currentTargetTag">A tag alvo original (pode ser a mesma que 'tag')</param>
        /// <param name="chipModel">O modelo do chip da tag</param>
        /// <param name="logFile">O caminho para o arquivo de log onde registrar os resultados</param>
        public void HandleVerifiedTagWithMetrics(Tag tag, string tidHex, string expectedEpc, Stopwatch swWrite, Stopwatch swVerify, ConcurrentDictionary<string, int> cycleCount, Tag currentTargetTag, string chipModel, string logFile)
        {
            // Chamar o método base para manter a funcionalidade principal
            HandleVerifiedTag(tag, tidHex, expectedEpc, swWrite, swVerify, cycleCount, currentTargetTag, chipModel, logFile);

            // Registrar os tempos para métricas
            RecordWriteTime(tidHex, swWrite.ElapsedMilliseconds);
            RecordVerifyTime(tidHex, swVerify.ElapsedMilliseconds);

            // Incrementar contadores para cálculo de taxas
            RecordTagRead();

            // Registrar métricas adicionais que podem ser úteis para análise
            try
            {
                // Registrar informações do modelo do chip se disponíveis
                if (!string.IsNullOrEmpty(chipModel))
                {
                    // Se temos um dicionário de métricas por modelo de chip, atualizar as contagens
                    // Este é um exemplo - você pode precisar implementar esta estrutura de dados
                    // _chipModelCounts.AddOrUpdate(chipModel, 1, (key, count) => count + 1);
                }

                // Registrar informações de RSSI se disponíveis
                if (tag.IsPeakRssiInDbmPresent)
                {
                    double rssi = tag.PeakRssiInDbm;
                    // Manter uma média móvel do RSSI ou outras estatísticas, se necessário
                    // _averageRssi = (_averageRssi * (_totalProcessedCount - 1) + rssi) / _totalProcessedCount;
                }

                // Registrar informações de número de porta da antena, se disponíveis
                if (tag.IsAntennaPortNumberPresent)
                {
                    ushort antennaPort = tag.AntennaPortNumber;
                    // Rastrear estatísticas por porta de antena, se necessário
                    // _antennaPortCounts.AddOrUpdate(antennaPort, 1, (key, count) => count + 1);
                }

                // Monitoramento de ciclos de retentativa
                if (cycleCount != null && cycleCount.TryGetValue(tidHex, out int cycles))
                {
                    // Registrar métricas sobre tentativas necessárias para sucesso
                    if (cycles > 1)
                    {
                        // _multiCycleSuccesses++;
                        // _totalRetryCount += (cycles - 1);
                    }
                }
            }
            catch (Exception ex)
            {
                // Registrar a exceção, mas não permitir que ela interrompa o fluxo
                Console.WriteLine($"Warning: Error recording extended metrics: {ex.Message}");
            }
        }
    }
}
