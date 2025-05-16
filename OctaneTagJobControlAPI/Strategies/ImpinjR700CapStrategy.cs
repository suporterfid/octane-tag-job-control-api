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
using static OctaneTagWritingTest.Helpers.TagOpController;

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
        // For CAP application specific caching
        private readonly ConcurrentDictionary<string, TagReadData> _lastReadTags = new ConcurrentDictionary<string, TagReadData>();
        private readonly ConcurrentDictionary<string, string> _pendingWrites = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, string> _pendingAccessPasswords = new ConcurrentDictionary<string, string>();

        private readonly Stopwatch _runTimer = new Stopwatch();
        private JobExecutionStatus _status = new JobExecutionStatus();
        private bool _lockEnabled = true;
        private bool _permalockEnabled = false;
        private string _readerID = "Tunnel-01";

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

            // Initialize the TagOpController - this is critical for tag operations
            TagOpController.Instance.CleanUp();

            // Extract configuration from reader settings
            if (readerSettings.TryGetValue("writer", out var writerSettings) &&
                writerSettings.Parameters != null)
            {
                if (writerSettings.Parameters.TryGetValue("enableLock", out var lockStr))
                {
                    _lockEnabled = bool.TryParse(lockStr, out bool lockVal) ? lockVal : true;
                }

                if (writerSettings.Parameters.TryGetValue("enablePermalock", out var permalockStr))
                {
                    _permalockEnabled = bool.TryParse(permalockStr, out bool permalockVal) ? permalockVal : false;
                }

                if (writerSettings.Parameters.TryGetValue("ReaderID", out var readerIDStr))
                {
                    _readerID = readerIDStr;
                }
            }
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
                        { "AccessMemory", TagOpController.Instance.IsTagLocked(tidHex) ? "locked" : "unlocked" },
                        { "ReaderID", _readerID },
                        { "EAN", TagOpController.Instance.GetEanFromEpc(epcHex) ?? "" }
                    }
                };

                // Cache the tag data
                _lastReadTags.AddOrUpdate(tidHex, tagData, (key, oldValue) =>
                {
                    oldValue.ReadCount++;
                    oldValue.Timestamp = DateTime.Now;
                    oldValue.RSSI = tag.PeakRssiInDbm;
                    oldValue.EPC = epcHex;
                    return oldValue;
                });

                // Report tag data to job manager
                ReportTagData(tagData);

                // Update job status
                lock (_status)
                {
                    _status.TotalTagsProcessed = _lastReadTags.Count;
                    _status.SuccessCount = _lastReadTags.Count;
                    _status.Metrics = new Dictionary<string, object>
                    {
                        { "UniqueTagsRead", _lastReadTags.Count },
                        { "LockedTags", TagOpController.Instance.GetLockedTagsCount() },
                        { "ReadRate", TagOpController.Instance.GetReadRate() },
                        { "ElapsedSeconds", _runTimer.Elapsed.TotalSeconds }
                    };
                }

                // Check if this tag has a pending write operation
                if (_manualWriteMode && _pendingWrites.TryGetValue(tidHex, out var newEpc))
                {
                    // Get access password
                    string accessPassword = "00000000";
                    _pendingAccessPasswords.TryGetValue(tidHex, out accessPassword);

                    Console.WriteLine($"Processing pending write for TID {tidHex}: {epcHex} -> {newEpc}");

                    // Use TagOpController to handle the write operation
                    TagOpController.Instance.TriggerWriteAndVerify(
                        tag,
                        newEpc,
                        reader,
                        cancellationToken,
                        new Stopwatch(), // TagOpController manages its own timers
                        accessPassword,
                        true, // Verify after writing
                        1,    // Antenna port
                        true, // Enable fast ID
                        3     // Number of retries
                    );

                    var writeSuccess = TagOpController.Instance.HasResult(tidHex);

                    // If successful and lock is enabled, use TagOpController to handle locking
                    if (writeSuccess && _lockEnabled)
                    {
                        if (_permalockEnabled)
                        {
                            TagOpController.Instance.PermaLockTag(tag, accessPassword, reader);
                        }
                        else
                        {
                            TagOpController.Instance.LockTag(tag, accessPassword, reader);
                        }
                    }

                    // Remove from pending lists
                    _pendingWrites.TryRemove(tidHex, out _);
                    _pendingAccessPasswords.TryRemove(tidHex, out _);
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

                // TagOpController will handle most of the logic, we just need to update our local cache
                var epcHex = result.Tag.Epc?.ToHexString() ?? "N/A";

                // Update tag data cache if available
                if (_lastReadTags.TryGetValue(tidHex, out var tagData))
                {
                    tagData.EPC = epcHex;

                    // Update lock status
                    if (result is TagLockOpResult)
                    {
                        tagData.AdditionalData["AccessMemory"] = "locked";
                    }

                    // Update EAN if needed
                    tagData.AdditionalData["EAN"] = TagOpController.Instance.GetEanFromEpc(epcHex) ?? "";
                }
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
                    { "AvgWriteTimeMs", TagOpController.Instance.GetAvgWriteTimeMs() },
                    { "AvgVerifyTimeMs", TagOpController.Instance.GetAvgVerifyTimeMs() },
                    { "LockedTags", TagOpController.Instance.GetLockedTagsCount() },
                    { "SuccessCount", TagOpController.Instance.GetSuccessCount() },
                    { "FailureCount", TagOpController.Instance.GetFailureCount() },
                    { "ElapsedSeconds", _runTimer.Elapsed.TotalSeconds },
                    { "ReaderHostname", hostname },
                    { "ReaderID", _readerID },
                    { "ReadRate", TagOpController.Instance.GetReadRate() },
                    { "LockEnabled", _lockEnabled },
                    { "PermalockEnabled", _permalockEnabled }
                };

                return new JobExecutionStatus
                {
                    TotalTagsProcessed = _status.TotalTagsProcessed,
                    SuccessCount = TagOpController.Instance.GetSuccessCount(),
                    FailureCount = TagOpController.Instance.GetFailureCount(),
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

            // Also record expected EPC in TagOpController for future verification
            TagOpController.Instance.RecordExpectedEpc(tid, newEpc);
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