using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Impinj.OctaneSdk;
using Impinj.TagUtils;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagWritingTest.Helpers;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Strategies.Base.Configuration;

namespace OctaneTagJobControlAPI.Strategies
{
    /// <summary>
    /// Single-reader strategy that reads tags during a configurable period, confirms the number of tags,
    /// generates new EPCs based on the selected encoding method, and writes them to the tags.
    /// </summary>
    [StrategyDescription(
        "Reads, encodes, and writes EPC data to tags in a checkbox-like flow controlled by GPI events",
        "Encoding",
        StrategyCapability.Reading | StrategyCapability.Writing | StrategyCapability.Verification | StrategyCapability.Encoding)]
    public class CheckBoxStrategy : SingleReaderStrategyBase
    {
        /// <summary>
        /// Encoding methods that the strategy supports
        /// </summary>
        public enum EpcEncodingMethod
        {
            /// <summary>
            /// Original method: header + SKU + TID suffix
            /// </summary>
            BasicWithTidSuffix,

            /// <summary>
            /// SGTIN-96 format
            /// </summary>
            SGTIN96,

            /// <summary>
            /// For future expansion
            /// </summary>
            CustomFormat
        }

        // Configuration constants
        private const int ReadDurationSeconds = 10;
        private const int WriteTimeoutSeconds = 20;
        private const int VerificationDurationMs = 5000;

        // Configuration parameters
        private readonly string _sku;
        private readonly string _epcHeader;
        private readonly EpcEncodingMethod _encodingMethod;
        private readonly int _partitionValue;
        private readonly int _itemReference;

        // Thread-safe dictionary to track for each tag its initial (original) EPC and current verified EPC
        private readonly ConcurrentDictionary<string, (string OriginalEpc, string VerifiedEpc)> _tagData
            = new ConcurrentDictionary<string, (string, string)>();

        // Cumulative count of successfully verified tags
        private int _successCount = 0;

        // Dictionary to capture full Tag objects for later processing
        private readonly ConcurrentDictionary<string, Tag> _collectedTags = new ConcurrentDictionary<string, Tag>();

        // Separate dictionary for capturing tags during the verification phase
        private readonly ConcurrentDictionary<string, Tag> _verificationTags = new ConcurrentDictionary<string, Tag>();

        // Flag to indicate if the GPI processing is already running
        private int _gpiProcessingFlag = 0;

        // This flag is also used to stop collecting further tags once the read period has elapsed
        private bool _isCollectingTags = true;

        // Flag to indicate that the verification phase is active
        private bool _isVerificationPhase = false;

        // Status tracking
        private readonly Stopwatch _runTimer = new Stopwatch();
        private JobExecutionStatus _status = new JobExecutionStatus();

        /// <summary>
        /// Initializes a new instance of the CheckBoxStrategy class
        /// </summary>
        /// <param name="hostname">Reader hostname</param>
        /// <param name="logFile">Path to log file</param>
        /// <param name="readerSettings">Dictionary of reader settings</param>
        /// <param name="epcHeader">EPC header value (e.g., "E7")</param>
        /// <param name="sku">SKU or GS1 company prefix</param>
        /// <param name="encodingMethod">Method to use for EPC encoding</param>
        /// <param name="partitionValue">SGTIN-96 partition value (0-6), defaults to 6</param>
        /// <param name="itemReference">SGTIN-96 item reference, defaults to 0</param>
        public CheckBoxStrategy(
            string hostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings,
            string epcHeader = "E7",
            string sku = null,
            string encodingMethod = "BasicWithTidSuffix",
            int partitionValue = 6,
            int itemReference = 0,
            IServiceProvider serviceProvider = null)
            : base(hostname, logFile, readerSettings,serviceProvider)
        {
            _epcHeader = epcHeader;
            _sku = sku ?? "012345678901";

            // Parse encoding method
            if (Enum.TryParse<EpcEncodingMethod>(encodingMethod, true, out var method))
            {
                _encodingMethod = method;
            }
            else
            {
                _encodingMethod = EpcEncodingMethod.BasicWithTidSuffix;
                Console.WriteLine($"Unrecognized encoding method '{encodingMethod}', defaulting to BasicWithTidSuffix");
            }

            // Check encoding method specific parameters
            if (_encodingMethod == EpcEncodingMethod.BasicWithTidSuffix && _sku.Length != 12)
            {
                Console.WriteLine("Warning: SKU should be 12 digits for BasicWithTidSuffix encoding");
            }

            // Set SGTIN-96 specific parameters
            _partitionValue = Math.Clamp(partitionValue, 0, 6);
            _itemReference = itemReference;

            // Initialize status
            _status.CurrentOperation = "Initialized";

            // Clean up any previous tag operation state
            TagOpController.Instance.CleanUp();
        }

        /// <summary>
        /// Executes the CheckBox strategy job
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                _status.CurrentOperation = "Starting";
                _runTimer.Start();

                Console.WriteLine("=== Single Reader CheckBox Test ===");
                Console.WriteLine($"Encoding Method: {_encodingMethod}");
                Console.WriteLine("GPI events on Port 1 will trigger tag collection, write, and verification. Press 'q' to cancel.");

                // Load any required EPC list
                EpcListManager.Instance.LoadEpcList("epc_list.txt");

                // Configure the reader and attach event handlers
                try
                {
                    ConfigureReaderWithGpi();
                }
                catch (Exception ex)
                {
                    _status.CurrentOperation = "Configuration Error";
                    Console.WriteLine($"Error during reader configuration in CheckBox strategy. {ex.Message}");
                    throw;
                }

                // Create CSV header if the log file does not exist
                if (!File.Exists(logFile))
                    LogToCsv("Timestamp,TID,Original_EPC,Expected_EPC,Verified_EPC,Encoding,Result");

                _status.CurrentOperation = "Waiting for GPI Trigger";

                // Keep the application running until the user cancels
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).KeyChar == 'q')
                    {
                        break;
                    }

                    Thread.Sleep(200); // Reduce CPU usage
                    _status.RunTime = _runTimer.Elapsed;
                }

                _status.CurrentOperation = "Stopping";
                Console.WriteLine("\nStopping test...");
            }
            catch (Exception ex)
            {
                _status.CurrentOperation = "Error";
                Console.WriteLine("Error in CheckBoxStrategy: " + ex.Message);
            }
            finally
            {
                _runTimer.Stop();
                CleanupReader();
            }
        }

        /// <summary>
        /// Gets the current status of the job execution
        /// </summary>
        public override JobExecutionStatus GetStatus()
        {
            lock (_status)
            {
                return new JobExecutionStatus
                {
                    TotalTagsProcessed = _tagData.Count,
                    SuccessCount = _successCount,
                    FailureCount = _tagData.Count - _successCount,
                    ProgressPercentage = _tagData.Count > 0
                        ? (double)_successCount / _tagData.Count * 100
                        : 0,
                    CurrentOperation = _status.CurrentOperation,
                    RunTime = _status.RunTime,
                    Metrics = new Dictionary<string, object>
                    {
                        { "EncodingMethod", _encodingMethod.ToString() },
                        { "CollectedTags", _collectedTags.Count },
                        { "VerifiedTags", _verificationTags.Count },
                        { "ElapsedSeconds", _runTimer.Elapsed.TotalSeconds },
                        { "SKU", _sku },
                        { "EpcHeader", _epcHeader }
                    }
                };
            }
        }

        /// <summary>
        /// Gets the metadata for this strategy
        /// </summary>
        public override StrategyMetadata GetMetadata()
        {
            return new StrategyMetadata
            {
                Name = "CheckBoxStrategy",
                Description = "Reads, encodes, and writes EPC data to tags in a checkbox-like flow controlled by GPI events",
                Category = "Encoding",
                ConfigurationType = typeof(EncodingStrategyConfiguration),
                Capabilities = StrategyCapability.Reading | StrategyCapability.Writing |
                    StrategyCapability.Verification | StrategyCapability.Encoding,
                RequiresMultipleReaders = false
            };
        }

        /// <summary>
        /// Configures the reader settings with GPI event handling
        /// </summary>
        private void ConfigureReaderWithGpi()
        {
            var writerSettings = GetSettingsForRole("writer");

            reader.Connect(writerSettings.Hostname);
            reader.ApplyDefaultSettings();

            var settingsToApply = reader.QueryDefaultSettings();
            settingsToApply.Report.IncludeFastId = writerSettings.IncludeFastId;
            settingsToApply.Report.IncludePeakRssi = writerSettings.IncludePeakRssi;
            settingsToApply.Report.IncludePcBits = true;
            settingsToApply.Report.IncludeAntennaPortNumber = writerSettings.IncludeAntennaPortNumber;
            settingsToApply.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), writerSettings.ReportMode);
            settingsToApply.RfMode = (uint)writerSettings.RfMode;
            settingsToApply.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), writerSettings.SearchMode);
            settingsToApply.Session = (ushort)writerSettings.Session;

            settingsToApply.Antennas.DisableAll();
            for (ushort port = 1; port <= 4; port++)
            {
                var antenna = settingsToApply.Antennas.GetAntenna(port);
                antenna.IsEnabled = true;
                antenna.TxPowerInDbm = writerSettings.TxPowerInDbm;
                antenna.MaxRxSensitivity = writerSettings.MaxRxSensitivity;
                antenna.RxSensitivityInDbm = writerSettings.RxSensitivityInDbm;
            }

            // Configure GPI for port 1
            var gpi = settingsToApply.Gpis.GetGpi(1);
            gpi.IsEnabled = true;
            gpi.DebounceInMs = 50;

            // Set GPI triggers for starting and stopping the operation
            settingsToApply.AutoStart.Mode = AutoStartMode.GpiTrigger;
            settingsToApply.AutoStart.GpiPortNumber = 1;
            settingsToApply.AutoStart.GpiLevel = true;
            settingsToApply.AutoStop.Mode = AutoStopMode.GpiTrigger;
            settingsToApply.AutoStop.GpiPortNumber = 1;
            settingsToApply.AutoStop.GpiLevel = false;

            // Attach event handlers, including our specialized GPI event handler
            reader.GpiChanged += OnGpiEvent;
            reader.TagsReported += OnTagsReported;
            reader.TagOpComplete += OnTagOpComplete;

            // Enable low latency reporting
            EnableLowLatencyReporting(settingsToApply);

            // Apply the settings
            reader.ApplySettings(settingsToApply);
            reader.Start();
        }

        /// <summary>
        /// Handles GPI events for the reader.
        /// Only processes events for Port 1.
        /// If the event State is true and not already processing, starts the tag collection flow.
        /// When the state is false, resets the processing flag.
        /// </summary>
        private async void OnGpiEvent(ImpinjReader sender, GpiEvent e)
        {
            if (e.PortNumber != 1)
                return;

            if (e.State)
            {
                // Use Interlocked.CompareExchange to ensure only one processing instance runs
                if (Interlocked.CompareExchange(ref _gpiProcessingFlag, 1, 0) == 0)
                {
                    Console.WriteLine("GPI Port 1 is TRUE - initiating tag collection and processing.");
                    _status.CurrentOperation = "Collecting Tags";

                    // Begin tag collection
                    bool collectionConfirmed = await WaitForReadTagsAsync();
                    if (collectionConfirmed)
                    {
                        _status.CurrentOperation = "Writing Tags";
                        // Proceed to execute write/verify operations once collection is done
                        await EncodeReadTagsAsync();

                        _status.CurrentOperation = "Verifying Tags";
                        // After writing, start the verification phase
                        await VerifyWrittenTagsAsync();

                        _status.CurrentOperation = "Cycle Complete";
                    }
                    else
                    {
                        _status.CurrentOperation = "Cancelled by User";
                    }
                    // Note: Do not reset the flag here. It will be reset when the GPI event goes to false.
                }
                else
                {
                    Console.WriteLine("GPI Port 1 event received while processing already in progress. Ignoring duplicate trigger.");
                }
            }
            else
            {
                // When GPI state becomes false, reset the processing flag
                Console.WriteLine("GPI Port 1 is FALSE - resetting processing flag.");
                CleanupTags();
                Interlocked.Exchange(ref _gpiProcessingFlag, 0);
                _status.CurrentOperation = "Waiting for GPI Trigger";
            }
        }

        /// <summary>
        /// Waits for the tag collection period to complete.
        /// At the end of the period, stops accepting new tags.
        /// </summary>
        private async Task<bool> WaitForReadTagsAsync()
        {
            _isCollectingTags = true;
            _collectedTags.Clear();
            _tagData.Clear();

            Console.WriteLine("Collecting tags for {0} seconds...", ReadDurationSeconds);
            try
            {
                await Task.Delay(ReadDurationSeconds * 1000, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Tag collection was canceled.");
                return false;
            }

            // End tag collection so that no new tags are accepted
            _isCollectingTags = false;

            // Update status with tag count
            lock (_status)
            {
                _status.TotalTagsProcessed = _tagData.Count;
            }

            Console.WriteLine("Tag collection ended. Total tags collected: {0}. Confirm? (y/n)", _tagData.Count);
            string confirmation = Console.ReadLine();
            if (!confirmation.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Operation canceled by user.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Iterates over all collected tags (from the collection phase) and triggers write/verification operations
        /// using the TagOpController.
        /// </summary>
        private async Task EncodeReadTagsAsync()
        {
            Console.WriteLine($"Starting write phase using {_encodingMethod} encoding...");
            Stopwatch globalWriteTimer = Stopwatch.StartNew();
            Stopwatch swWrite = new Stopwatch();

            foreach (var kvp in _collectedTags)
            {
                if (globalWriteTimer.Elapsed.TotalSeconds > WriteTimeoutSeconds)
                {
                    Console.WriteLine("Global write timeout reached.");
                    break;
                }

                Tag tag = kvp.Value;
                string tid = tag.Tid.ToHexString();
                string originalEpc = tag.Epc.ToHexString();

                // Generate EPC based on selected encoding method
                string newEpc = GenerateEpc(tag);

                TagOpController.Instance.RecordExpectedEpc(tid, newEpc);

                // Trigger the write operation using the writer reader
                TagOpController.Instance.TriggerWriteAndVerify(
                    tag,
                    newEpc,
                    reader,
                    cancellationToken,
                    swWrite,
                    newAccessPassword,
                    true,
                    1,
                    true,
                    3);

                // Optionally, delay briefly between processing tags
                await Task.Delay(100, cancellationToken);

                // Record the tag data
                _tagData.AddOrUpdate(tid, (originalEpc, newEpc), (key, old) => (originalEpc, newEpc));
            }

            globalWriteTimer.Stop();
        }

        /// <summary>
        /// Generates an EPC for a tag based on the selected encoding method
        /// </summary>
        /// <param name="tag">The tag to generate an EPC for</param>
        /// <returns>The generated EPC string</returns>
        private string GenerateEpc(Tag tag)
        {
            string tid = tag.Tid.ToHexString();
            string originalEpc = tag.Epc.ToHexString();

            switch (_encodingMethod)
            {
                case EpcEncodingMethod.SGTIN96:
                    try
                    {
                        // Use the Sgtin96 class to generate an SGTIN-96 encoded EPC
                        string gtin = _sku;
                        if (gtin.Length < 13)
                        {
                            // Pad the SKU to at least 13 characters if needed
                            gtin = gtin.PadLeft(13, '0');
                        }

                        // Create SGTIN-96 object
                        var sgtin96 = Sgtin96.FromGTIN(gtin, _partitionValue);

                        // Set serial number based on TID (use last 10 digits of TID as a numeric value)
                        string serialStr = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid;

                        // Try to convert the serial to a number (use TID-based hash if conversion fails)
                        if (ulong.TryParse(serialStr, System.Globalization.NumberStyles.HexNumber, null, out ulong serialNumber))
                        {
                            // Make sure the serial doesn't exceed 38-bit max value (2^38-1)
                            serialNumber = Math.Min(serialNumber, 274877906943); // 2^38-1
                        }
                        else
                        {
                            // Use a hash of the TID as a fallback
                            serialNumber = (ulong)Math.Abs(tid.GetHashCode()) % 274877906943;
                        }

                        sgtin96.SerialNumber = serialNumber;

                        // Convert to hexadecimal EPC
                        string newEpc = sgtin96.ToEpc();

                        Console.WriteLine($"Generated SGTIN-96 EPC for TID {tid}: {newEpc}");
                        return newEpc;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error generating SGTIN-96 EPC: {ex.Message}. Falling back to basic encoding.");
                        // Fall back to basic encoding if SGTIN-96 fails
                        return GenerateBasicEpcWithTidSuffix(tid);
                    }

                case EpcEncodingMethod.CustomFormat:
                    // Placeholder for future custom encoding methods
                    Console.WriteLine("CustomFormat encoding not yet implemented, falling back to basic encoding.");
                    return GenerateBasicEpcWithTidSuffix(tid);

                case EpcEncodingMethod.BasicWithTidSuffix:
                default:
                    return GenerateBasicEpcWithTidSuffix(tid);
            }
        }

        /// <summary>
        /// Generates a basic EPC using header + SKU + TID suffix format
        /// </summary>
        private string GenerateBasicEpcWithTidSuffix(string tid)
        {
            string tidSuffix = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
            string newEpc = _epcHeader + _sku + tidSuffix;
            Console.WriteLine($"Generated basic EPC for TID {tid}: {newEpc}");
            return newEpc;
        }

        /// <summary>
        /// Event handler for tag reports.
        /// In normal collection mode, accepts new tags if isCollectingTags is true and they haven't been processed.
        /// In verification mode, stores all tag reports into the verificationTags dictionary.
        /// </summary>
        private void OnTagsReported(object sender, TagReport report)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (_isVerificationPhase)
            {
                foreach (var tag in report.Tags)
                {
                    string tid = tag.Tid?.ToHexString() ?? "";
                    string epc = tag.Epc?.ToHexString() ?? "";
                    if (!string.IsNullOrEmpty(tid))
                    {
                        _verificationTags.TryAdd(tid, tag);
                        Console.WriteLine("Verification read: TID: {0}, EPC: {1}", tid, epc);
                    }
                }
            }
            else
            {
                if (!_isCollectingTags)
                    return;

                foreach (var tag in report.Tags)
                {
                    string tid = tag.Tid?.ToHexString() ?? "";
                    string epc = tag.Epc?.ToHexString() ?? "";

                    if (string.IsNullOrEmpty(tid) || TagOpController.Instance.IsTidProcessed(tid))
                        continue;

                    _collectedTags.TryAdd(tid, tag);
                    _tagData.AddOrUpdate(tid, (epc, string.Empty), (key, old) => (epc, old.VerifiedEpc));

                    Console.WriteLine("Detected Tag: TID: {0}, EPC: {1}, Antenna: {2}", tid, epc, tag.AntennaPortNumber);

                    // Update status with tag count
                    lock (_status)
                    {
                        _status.TotalTagsProcessed = _tagData.Count;
                    }
                }
            }
        }

        /// <summary>
        /// Handles tag operation completion events
        /// </summary>
        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || cancellationToken.IsCancellationRequested)
                return;

            foreach (TagOpResult result in report)
            {
                string tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";

                if (result is TagWriteOpResult writeResult)
                {
                    string oldEpc = writeResult.Tag.Epc.ToHexString();
                    string newEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    string resultStatus = writeResult.Result.ToString();

                    Console.WriteLine($"Write operation for TID {tidHex}: {resultStatus}");

                    bool success = resultStatus == "Success";

                    // If this is a success, increment the counter
                    if (success)
                    {
                        _successCount++;

                        // Update status with success count
                        lock (_status)
                        {
                            _status.SuccessCount = _successCount;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Starts a verification phase after writing: re-enables tag report collection,
        /// waits for a fixed period, then compares reported EPC values with the expected EPCs.
        /// </summary>
        private async Task VerifyWrittenTagsAsync()
        {
            Console.WriteLine("Starting verification phase...");

            // Prepare for verification
            _verificationTags.Clear();
            _isVerificationPhase = true;

            // Wait for the verification period
            try
            {
                await Task.Delay(VerificationDurationMs, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Verification phase was canceled.");
                _isVerificationPhase = false;
                return;
            }

            _isVerificationPhase = false;

            int verifiedCount = 0;
            foreach (var kvp in _verificationTags)
            {
                string tid = kvp.Key;
                string reportedEpc = kvp.Value.Epc?.ToHexString() ?? "";

                if (_tagData.TryGetValue(tid, out var data))
                {
                    string originalEpc = data.OriginalEpc;
                    string expectedEpc = TagOpController.Instance.GetExpectedEpc(tid) ?? "";

                    bool success = string.Equals(reportedEpc, expectedEpc, StringComparison.OrdinalIgnoreCase);
                    string status = success ? "Success" : "Failure";

                    if (success)
                    {
                        verifiedCount++;
                        Console.WriteLine($"Verification SUCCESS: TID {tid} reported EPC {reportedEpc}");
                    }
                    else
                    {
                        Console.WriteLine($"Verification FAILURE: TID {tid} expected EPC {expectedEpc} but got {reportedEpc}");
                    }

                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tid},{originalEpc},{expectedEpc},{reportedEpc},{_encodingMethod},{status}");

                    // Don't record the result again if already processed
                    if (!TagOpController.Instance.IsTidProcessed(tid))
                    {
                        TagOpController.Instance.RecordResult(tid, status, success);
                    }
                }
                else
                {
                    Console.WriteLine($"Verification: No data recorded for TID {tid}");
                }
            }

            // Update status with final verification results
            lock (_status)
            {
                _status.SuccessCount = verifiedCount;
                _status.FailureCount = _tagData.Count - verifiedCount;
                _status.ProgressPercentage = _tagData.Count > 0
                    ? (double)verifiedCount / _tagData.Count * 100
                    : 0;
            }

            Console.WriteLine($"Verification complete: {verifiedCount} / {_tagData.Count} tags verified successfully.");
        }

        /// <summary>
        /// Cleans up tag collections after a cycle
        /// </summary>
        private void CleanupTags()
        {
            try
            {
                Console.WriteLine("Cleaning up tag collections...");
                _collectedTags.Clear();
                _verificationTags.Clear();
                _isCollectingTags = false;
                _isVerificationPhase = false;

                // Do not clear _tagData or reset _successCount here as they contain 
                // the cumulative results across all cycles

                Console.WriteLine("Tag collections cleared.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during tag cleanup: " + ex.Message);
            }
        }

        /// <summary>
        /// Cleans up resources before disposing
        /// </summary>
        public override void Dispose()
        {
            CleanupTags();
            base.Dispose();
        }
    }
}