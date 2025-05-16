using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Impinj.OctaneSdk;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagJobControlAPI.Strategies.Base.Configuration;
using OctaneTagWritingTest.Helpers;

namespace OctaneTagJobControlAPI.Strategies
{
    /// <summary>
    /// Strategy for Impinj R700 RFID reader implementing the CAP application requirements
    /// </summary>
    [StrategyDescription(
        "Impinj R700 CAP application with REST-based tag reading and writing",
        "RFID CAP",
        StrategyCapability.Reading | StrategyCapability.Writing | StrategyCapability.Verification |
        StrategyCapability.MultiAntenna | StrategyCapability.Permalock | StrategyCapability.Encoding)]
    public class ImpinjR700CapStrategy : SingleReaderStrategyBase
    {
        // Configuration 
        private readonly ConcurrentDictionary<string, Stopwatch> _writeTimers = new ConcurrentDictionary<string, Stopwatch>();
        private readonly ConcurrentDictionary<string, Stopwatch> _verifyTimers = new ConcurrentDictionary<string, Stopwatch>();
        private readonly ConcurrentDictionary<string, Stopwatch> _lockTimers = new ConcurrentDictionary<string, Stopwatch>();
        private readonly ConcurrentDictionary<string, bool> _lockedTags = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, string> _pendingWrites = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _pendingAccessPasswords = new ConcurrentDictionary<string, string>();

        private readonly Stopwatch _runTimer = new Stopwatch();
        private JobExecutionStatus _status = new JobExecutionStatus();
        private bool _lockEnabled = true;

        // For CAP application specific caching
        private readonly ConcurrentDictionary<string, TagReadData> _lastReadTags = new ConcurrentDictionary<string, TagReadData>();

        // Fields for manual writing mode
        private bool _manualWriteMode = false;

        /// <summary>
        /// Initializes a new instance of the ImpinjR700CapStrategy class
        /// </summary>
        /// <param name="hostname">The hostname of the RFID reader</param>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="readerSettings">Dictionary of reader settings</param>
        /// <param name="serviceProvider">Service provider for dependency injection</param>
        public ImpinjR700CapStrategy(
            string hostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings,
            IServiceProvider serviceProvider = null)
            : base(hostname, logFile, readerSettings, serviceProvider)
        {
            _status.CurrentOperation = "Initialized";
            TagOpController.Instance.CleanUp();
        }

        /// <summary>
        /// Executes the job with continuous tag reading based on configuration
        /// </summary>
        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                _status.CurrentOperation = "Starting";

                Console.WriteLine("Starting Impinj R700 CAP Strategy...");
                _runTimer.Start();

                var readerSettings = GetSettingsForRole("writer");
                _lockEnabled = true;

                if (settings.TryGetValue("writer", out var writerSettings) &&
                    writerSettings.Parameters != null &&
                    writerSettings.Parameters.TryGetValue("enableLock", out var lockStr))
                {
                    _lockEnabled = bool.TryParse(lockStr, out bool lockVal) ? lockVal : true;
                }

                // Configure reader
                ConfigureReader();

                // Register event handlers
                reader.TagsReported += OnTagsReported;
                reader.TagOpComplete += OnTagOpComplete;

                // Create CSV header if needed
                if (!File.Exists(logFile))
                {
                    LogToCsv("Timestamp,TID,EPC,NewEPC,VerifiedEPC,WriteTimeMs,VerifyTimeMs,Status,LockStatus,LockTimeMs,AntennaID,RSSI");
                }

                // Start reader
                reader.Start();
                _status.CurrentOperation = "Reading Tags";

                LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},System,System,System,System,0,0,Started,None,0,0,0");

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Keep the job running until cancellation
                    Thread.Sleep(100);
                    _status.RunTime = _runTimer.Elapsed;
                }

                _status.CurrentOperation = "Stopping";
                Console.WriteLine("\nStopping Impinj R700 CAP...");
            }
            catch (Exception ex)
            {
                _status.CurrentOperation = "Error";
                Console.WriteLine("Error in Impinj R700 CAP strategy: " + ex.Message);
                LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},Error,Error,Error,Error,0,0,{ex.Message},None,0,0,0");
            }
            finally
            {
                _runTimer.Stop();
                CleanupReader();
            }
        }

        /// <summary>
        /// Event handler for tag reports from the reader
        /// </summary>
        private void OnTagsReported(ImpinjReader sender, TagReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (var tag in report.Tags)
            {
                // Get TID and EPC
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                var epcHex = tag.Epc?.ToHexString() ?? string.Empty;

                // Skip if TID is empty
                if (string.IsNullOrEmpty(tidHex))
                    continue;

                // Create or update tag data
                var tagData = new TagReadData
                {
                    TID = tidHex,
                    EPC = epcHex,
                    RSSI = tag.PeakRssiInDbm,
                    AntennaPort = tag.AntennaPortNumber,
                    Timestamp = DateTime.Now,
                    ReadCount = 1,
                    PcBits = tag.PcBits.ToString() ?? string.Empty,
                    AdditionalData = new Dictionary<string, object>
                    {
                        { "FastID", tag.IsFastIdPresent },
                        { "Phase", tag.PhaseAngleInRadians },
                        { "ChannelInMhz", tag.ChannelInMhz },
                        { "AccessMemory", _lockedTags.ContainsKey(tidHex) ? "locked" : "unlocked" },
                        { "ReaderID", hostname }
                    }
                };

                // Cache the tag data
                _lastReadTags.AddOrUpdate(tidHex, tagData, (key, oldValue) => {
                    oldValue.ReadCount++;
                    oldValue.Timestamp = DateTime.Now;
                    oldValue.RSSI = tag.PeakRssiInDbm;
                    oldValue.EPC = epcHex;
                    return oldValue;
                });

                // Report tag data to job manager
                ReportTagData(tagData);

                lock (_status)
                {
                    _status.TotalTagsProcessed = _lastReadTags.Count;
                    _status.SuccessCount = _lastReadTags.Count;
                }

                // Check if this tag has a pending write operation
                if (_manualWriteMode && _pendingWrites.TryGetValue(tidHex, out var newEpc))
                {
                    string accessPassword = "00000000";
                    _pendingAccessPasswords.TryGetValue(tidHex, out accessPassword);

                    Console.WriteLine($"Processing pending write for TID {tidHex}: {epcHex} -> {newEpc}");

                    // Start write timer
                    var writeTimer = _writeTimers.GetOrAdd(tidHex, _ => new Stopwatch());
                    writeTimer.Restart();

                    // Configure and execute tag operations
                    var tagOp = new TagOp();
                    var writeEpc = new TagWriteOp();
                    writeEpc.MemoryBank = MemoryBank.Epc;
                    writeEpc.WordPointer = 2;
                    writeEpc.Data = TagData.FromHexString(newEpc);

                    // Use the access password if provided
                    if (!string.IsNullOrEmpty(accessPassword))
                    {
                        writeEpc.AccessPassword = Convert.ToUInt32(accessPassword, 16);
                    }

                    // Execute the write operation
                    try
                    {
                        sender.ExecuteTagOp(writeEpc, tag);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error writing to tag: {ex.Message}");
                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{newEpc},Error,{writeTimer.ElapsedMilliseconds},0,WriteError,None,0,{tag.AntennaPortNumber},{tag.PeakRssiInDbm}");
                    }

                    // Remove from pending writes
                    _pendingWrites.TryRemove(tidHex, out _);
                }
            }
        }

        /// <summary>
        /// Event handler for tag operation completions
        /// </summary>
        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (TagOpResult result in report)
            {
                var tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";

                // Handle write operation result
                if (result is TagWriteOpResult writeResult)
                {
                    // Stop write timer
                    _writeTimers.TryGetValue(tidHex, out var writeTimer);
                    if (writeTimer != null)
                    {
                        writeTimer.Stop();
                    }

                    var verifiedEpc = writeResult.Tag.Epc?.ToHexString() ?? "N/A";
                    var newEpc = _pendingWrites.TryGetValue(tidHex, out var epc) ? epc : verifiedEpc;
                    var writeStatus = writeResult.Result == WriteResultStatus.Success ? "Success" : "Failure";

                    Console.WriteLine($"Write operation for TID {tidHex}: {writeStatus}, EPC: {verifiedEpc}");
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{newEpc},{verifiedEpc},{writeTimer?.ElapsedMilliseconds ?? 0},0,{writeStatus},None,0,{result.Tag.AntennaPortNumber},{result.Tag.PeakRssiInDbm}");

                    // If successful and lock enabled, perform lock operation
                    if (writeResult.Result == WriteResultStatus.Success && _lockEnabled &&
                        _pendingAccessPasswords.TryGetValue(tidHex, out var accessPassword))
                    {
                        LockTag(result.Tag, accessPassword);
                    }

                    // Update tag data cache
                    if (_lastReadTags.TryGetValue(tidHex, out var tagData))
                    {
                        tagData.EPC = verifiedEpc;
                        tagData.AdditionalData["WriteStatus"] = writeStatus;
                    }

                    // Remove from pending lists
                    _pendingWrites.TryRemove(tidHex, out _);
                    _pendingAccessPasswords.TryRemove(tidHex, out _);
                }
                // Handle lock operation result
                else if (result is TagLockOpResult lockResult)
                {
                    // Stop lock timer
                    _lockTimers.TryGetValue(tidHex, out var lockTimer);
                    if (lockTimer != null)
                    {
                        lockTimer.Stop();
                    }

                    bool success = lockResult.Result == LockResultStatus.Success;
                    string lockStatus = success ? "Locked" : "LockFailed";

                    Console.WriteLine($"Lock operation for TID {tidHex}: {lockStatus}");
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},None,None,0,0,{lockStatus},Locked,{lockTimer?.ElapsedMilliseconds ?? 0},{result.Tag.AntennaPortNumber},{result.Tag.PeakRssiInDbm}");

                    // Update lock status in cache
                    if (success)
                    {
                        _lockedTags[tidHex] = true;

                        if (_lastReadTags.TryGetValue(tidHex, out var tagData))
                        {
                            tagData.AdditionalData["AccessMemory"] = "locked";
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Locks a tag using its access password
        /// </summary>
        private void LockTag(Tag tag, string accessPassword)
        {
            var tidHex = tag.Tid?.ToHexString() ?? string.Empty;

            if (string.IsNullOrEmpty(tidHex))
                return;

            // Start lock timer
            var lockTimer = _lockTimers.GetOrAdd(tidHex, _ => new Stopwatch());
            lockTimer.Restart();

            // Create lock operation
            var lockOp = new TagLockOp();
            lockOp.AccessPassword = Convert.ToUInt32(accessPassword, 16);

            // Lock EPC memory
            lockOp.LockMask = TagLockOp.GenerateLockMask(
                MemoryBank.Epc, TagLockOp.LockType.PermalockType, true);
            lockOp.LockMask |= TagLockOp.GenerateLockMask(
                MemoryBank.Epc, TagLockOp.LockType.PermalockType, true);

            // Execute lock operation
            try
            {
                reader.ExecuteTagOp(lockOp, tag);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error locking tag: {ex.Message}");
                LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{tag.Epc.ToHexString()},None,None,0,0,LockError,None,{lockTimer.ElapsedMilliseconds},{tag.AntennaPortNumber},{tag.PeakRssiInDbm}");
                lockTimer.Stop();
            }
        }

        /// <summary>
        /// Gets the current status of the job
        /// </summary>
        public override JobExecutionStatus GetStatus()
        {
            lock (_status)
            {
                var metrics = new Dictionary<string, object>
                {
                    { "UniqueTagsRead", _lastReadTags.Count },
                    { "AvgWriteTimeMs", _writeTimers.Count > 0 ? _writeTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0 },
                    { "AvgLockTimeMs", _lockTimers.Count > 0 ? _lockTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0 },
                    { "LockedTags", _lockedTags.Count },
                    { "ElapsedSeconds", _runTimer.Elapsed.TotalSeconds },
                    { "ReaderHostname", hostname }
                };

                return new JobExecutionStatus
                {
                    TotalTagsProcessed = _status.TotalTagsProcessed,
                    SuccessCount = _status.SuccessCount,
                    FailureCount = _status.FailureCount,
                    ProgressPercentage = _status.ProgressPercentage,
                    CurrentOperation = _status.CurrentOperation,
                    RunTime = _status.RunTime,
                    Metrics = metrics
                };
            }
        }

        /// <summary>
        /// Gets all current tags in the field
        /// </summary>
        public List<TagReadData> GetAllTags()
        {
            return _lastReadTags.Values.ToList();
        }

        /// <summary>
        /// Writes a new EPC to a tag with a specified TID
        /// </summary>
        public void WriteTagByTid(string tid, string newEpc, string accessPassword)
        {
            if (string.IsNullOrEmpty(tid) || string.IsNullOrEmpty(newEpc))
                return;

            // Store in pending writes dictionary to be processed when tag is read
            _pendingWrites[tid] = newEpc;

            if (!string.IsNullOrEmpty(accessPassword))
            {
                _pendingAccessPasswords[tid] = accessPassword;
            }

            // Enable manual write mode
            _manualWriteMode = true;

            // Log the pending write
            LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tid},PendingWrite,{newEpc},None,0,0,Pending,None,0,0,0");
        }

        /// <summary>
        /// Returns metadata about this strategy
        /// </summary>
        public override StrategyMetadata GetMetadata()
        {
            return new StrategyMetadata
            {
                Name = "ImpinjR700CapStrategy",
                Description = "Impinj R700 CAP application with REST-based tag reading and writing",
                Category = "RFID CAP",
                ConfigurationType = typeof(EncodingStrategyConfiguration),
                Capabilities = StrategyCapability.Reading | StrategyCapability.Writing |
                    StrategyCapability.Verification | StrategyCapability.MultiAntenna |
                    StrategyCapability.Permalock | StrategyCapability.Encoding,
                RequiresMultipleReaders = false
            };
        }
    }
}
