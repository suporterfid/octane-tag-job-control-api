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
using OctaneTagWritingTest.Helpers;
using Org.LLRP.LTK.LLRPV1.Impinj;

namespace OctaneTagWritingTest.JobStrategies
{
    /// <summary> 
    /// Single-reader "CheckBox" strategy with flexible EPC encoding support.
    /// This strategy uses a single reader configured with four antennas. 
    /// It reads tags for a configurable period, then confirms the number of tags, 
    /// generates new EPCs based on the selected encoding method, and writes them to the tags.
    /// The writing phase runs until all collected tags are written or until a write timeout is reached. 
    /// At the end, it verifies each tag's new EPC and reports the results. 
    /// </summary> 
    public class JobStrategy9CheckBox : BaseTestStrategy
    {
        // Encoding methods that the strategy supports
        public enum EpcEncodingMethod
        {
            BasicWithTidSuffix, // Original method: header + SKU + TID suffix
            SGTIN96,           // SGTIN-96 format
            CustomFormat       // For future expansion
        }

        private ImpinjReader writerReader;

        // Duration for tag collection (in seconds)
        private const int ReadDurationSeconds = 10;

        // Overall timeout for write operations (in seconds)
        private const int WriteTimeoutSeconds = 20;

        // Duration (in ms) for verification read phase
        private const int VerificationDurationMs = 5000;

        // The SKU or GS1 company prefix to use in the EPC
        private readonly string sku;

        // The EPC header to use
        private readonly string epcHeader;

        // The encoding method to use
        private readonly EpcEncodingMethod encodingMethod;

        // SGTIN specific parameters
        private readonly int partitionValue = 6;  // Default partition value
        private readonly int itemReference = 0;   // Default item reference

        // Thread-safe dictionary to track for each tag its initial (original) EPC and current verified EPC
        private readonly ConcurrentDictionary<string, (string OriginalEpc, string VerifiedEpc)> tagData
            = new ConcurrentDictionary<string, (string, string)>();

        // Cumulative count of successfully verified tags
        private int successCount = 0;

        // Dictionary to capture full Tag objects for later processing
        private readonly ConcurrentDictionary<string, Tag> collectedTags = new ConcurrentDictionary<string, Tag>();

        // Separate dictionary for capturing tags during the verification phase
        private readonly ConcurrentDictionary<string, Tag> verificationTags = new ConcurrentDictionary<string, Tag>();

        // Flag to indicate if the GPI processing is already running
        private int gpiProcessingFlag = 0;

        // This flag is also used to stop collecting further tags once the read period has elapsed
        private bool isCollectingTags = true;

        // Flag to indicate that the verification phase is active
        private bool isVerificationPhase = false;

        /// <summary>
        /// Constructor with support for different encoding methods
        /// </summary>
        /// <param name="hostname">Reader hostname</param>
        /// <param name="logFile">Path to log file</param>
        /// <param name="readerSettings">Dictionary of reader settings</param>
        /// <param name="epcHeader">EPC header value (e.g., "E7")</param>
        /// <param name="sku">SKU or GS1 company prefix</param>
        /// <param name="encodingMethod">Method to use for EPC encoding</param>
        /// <param name="partitionValue">SGTIN-96 partition value (0-6), defaults to 6</param>
        /// <param name="itemReference">SGTIN-96 item reference, defaults to 0</param>
        public JobStrategy9CheckBox(
            string hostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings,
            string epcHeader = "E7",
            string sku = null,
            string encodingMethod = "BasicWithTidSuffix",
            int partitionValue = 6,
            int itemReference = 0)
            : base(hostname, logFile, readerSettings)
        {
            this.epcHeader = epcHeader;
            this.sku = sku ?? "012345678901";

            // Parse encoding method
            if (Enum.TryParse<EpcEncodingMethod>(encodingMethod, true, out var method))
            {
                this.encodingMethod = method;
            }
            else
            {
                this.encodingMethod = EpcEncodingMethod.BasicWithTidSuffix;
                Console.WriteLine($"Unrecognized encoding method '{encodingMethod}', defaulting to BasicWithTidSuffix");
            }

            // Check encoding method specific parameters
            if (this.encodingMethod == EpcEncodingMethod.BasicWithTidSuffix && this.sku.Length != 12)
            {
                Console.WriteLine("Warning: SKU should be 12 digits for BasicWithTidSuffix encoding");
            }

            // Set SGTIN-96 specific parameters
            this.partitionValue = Math.Clamp(partitionValue, 0, 6);
            this.itemReference = itemReference;

            // Clean up any previous tag operation state
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                Console.WriteLine("=== Single Reader CheckBox Test ===");
                Console.WriteLine($"Encoding Method: {encodingMethod}");
                Console.WriteLine("GPI events on Port 1 will trigger tag collection, write, and verification. Press 'q' to cancel.");

                // Load any required EPC list
                EpcListManager.Instance.LoadEpcList("epc_list.txt");

                // Configure the reader and attach event handlers
                try
                {
                    ConfigureWriterReader();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during writer reader configuration in CheckBox strategy. {ex.Message}");
                    throw;
                }

                // Create CSV header if the log file does not exist
                if (!File.Exists(logFile))
                    LogToCsv("Timestamp,TID,Original_EPC,Expected_EPC,Verified_EPC,Encoding,Result");

                // Keep the application running until the user cancels
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).KeyChar == 'q')
                    {
                        break;
                    }
                    Thread.Sleep(200); // Reduce CPU usage
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in JobStrategy9CheckBox: " + ex.Message);
            }
            finally
            {
                CleanupWriterReader();
            }
        }

        /// <summary>
        /// Configures the reader settings and attaches all necessary event handlers
        /// </summary>
        private void ConfigureWriterReader()
        {
            var writerSettings = GetSettingsForRole("writer");
            if (writerReader == null)
                writerReader = new ImpinjReader();

            if (!writerReader.IsConnected)
            {
                writerReader.Connect(writerSettings.Hostname);
            }

            writerReader.ApplyDefaultSettings();

            var settingsToApply = writerReader.QueryDefaultSettings();
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
            writerReader.GpiChanged += OnGpiEvent;
            writerReader.TagsReported += OnTagsReported;

            // Enable low latency reporting
            EnableLowLatencyReporting(settingsToApply, writerReader);
        }

        private void EnableLowLatencyReporting(Settings settings, ImpinjReader reader)
        {
            var addRoSpecMessage = reader.BuildAddROSpecMessage(settings);
            var setReaderConfigMessage = reader.BuildSetReaderConfigMessage(settings);
            setReaderConfigMessage.AddCustomParameter(new PARAM_ImpinjReportBufferConfiguration()
            {
                ReportBufferMode = ENUM_ImpinjReportBufferMode.Low_Latency
            });
            reader.ApplySettings(setReaderConfigMessage, addRoSpecMessage);
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
                if (Interlocked.CompareExchange(ref gpiProcessingFlag, 1, 0) == 0)
                {
                    Console.WriteLine("GPI Port 1 is TRUE - initiating tag collection and processing.");
                    // Begin tag collection
                    bool collectionConfirmed = await WaitForReadTagsAsync();
                    if (collectionConfirmed)
                    {
                        // Proceed to execute write/verify operations once collection is done
                        await EncodeReadTagsAsync();
                        // After writing, start the verification phase
                        await VerifyWrittenTagsAsync();
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
                CleanupWriterReader();
                Interlocked.Exchange(ref gpiProcessingFlag, 0);
            }
        }

        /// <summary>
        /// Waits for the tag collection period to complete.
        /// At the end of the period, stops accepting new tags.
        /// </summary>
        private async Task<bool> WaitForReadTagsAsync()
        {
            isCollectingTags = true;
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
            isCollectingTags = false;

            Console.WriteLine("Tag collection ended. Total tags collected: {0}. Confirm? (y/n)", tagData.Count);
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
            Console.WriteLine($"Starting write phase using {encodingMethod} encoding...");
            Stopwatch globalWriteTimer = Stopwatch.StartNew();
            Stopwatch swWrite = new Stopwatch();

            foreach (var kvp in collectedTags)
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
                    writerReader,
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
                tagData.AddOrUpdate(tid, (originalEpc, newEpc), (key, old) => (originalEpc, newEpc));
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

            switch (encodingMethod)
            {
                case EpcEncodingMethod.SGTIN96:
                    try
                    {
                        // Use the Sgtin96 class to generate an SGTIN-96 encoded EPC
                        string gtin = sku;
                        if (gtin.Length < 13)
                        {
                            // Pad the SKU to at least 13 characters if needed
                            gtin = gtin.PadLeft(13, '0');
                        }

                        // Create SGTIN-96 object
                        var sgtin96 = Sgtin96.FromGTIN(gtin, partitionValue);

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
            string newEpc = epcHeader + sku + tidSuffix;
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

            if (isVerificationPhase)
            {
                foreach (var tag in report.Tags)
                {
                    string tid = tag.Tid?.ToHexString() ?? "";
                    string epc = tag.Epc?.ToHexString() ?? "";
                    if (!string.IsNullOrEmpty(tid))
                    {
                        verificationTags.TryAdd(tid, tag);
                        Console.WriteLine("Verification read: TID: {0}, EPC: {1}", tid, epc);
                    }
                }
            }
            else
            {
                if (!isCollectingTags)
                    return;
                foreach (var tag in report.Tags)
                {
                    string tid = tag.Tid?.ToHexString() ?? "";
                    string epc = tag.Epc?.ToHexString() ?? "";
                    if (string.IsNullOrEmpty(tid) || TagOpController.Instance.IsTidProcessed(tid))
                        continue;
                    collectedTags.TryAdd(tid, tag);
                    tagData.AddOrUpdate(tid, (epc, string.Empty), (key, old) => (epc, old.VerifiedEpc));
                    Console.WriteLine("Detected Tag: TID: {0}, EPC: {1}, Antenna: {2}", tid, epc, tag.AntennaPortNumber);
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
            verificationTags.Clear();
            isVerificationPhase = true;

            // Wait for the verification period
            try
            {
                await Task.Delay(VerificationDurationMs, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Verification phase was canceled.");
                isVerificationPhase = false;
                return;
            }
            isVerificationPhase = false;

            int verifiedCount = 0;
            foreach (var kvp in verificationTags)
            {
                string tid = kvp.Key;
                string reportedEpc = kvp.Value.Epc?.ToHexString() ?? "";

                if (tagData.TryGetValue(tid, out var data))
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

                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tid},{originalEpc},{expectedEpc},{reportedEpc},{encodingMethod},{status}");
                    TagOpController.Instance.RecordResult(tid, status, success);
                }
                else
                {
                    Console.WriteLine($"Verification: No data recorded for TID {tid}");
                }
            }

            Console.WriteLine($"Verification complete: {verifiedCount} / {tagData.Count} tags verified successfully.");
        }

        /// <summary>
        /// Cleans up reader resources, stops any running timers, and detaches event handlers.
        /// </summary>
        private void CleanupWriterReader()
        {
            try
            {
                Console.WriteLine("CleanupWriterReader running... ");
                if (sw != null && sw.IsRunning)
                {
                    sw.Stop();
                    sw.Reset();
                }

                // Clear tag collections
                collectedTags.Clear();
                verificationTags.Clear();
                tagData.Clear();
                TagOpController.Instance.CleanUp();
                Console.WriteLine("CleanupWriterReader done. ");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during CleanupWriterReader: " + ex.Message);
            }
        }

        /// <summary>
        /// Appends a line to the CSV log file.
        /// </summary>
        /// <param name="line">The CSV line to append.</param>
        private void LogToCsv(string line)
        {
            TagOpController.Instance.LogToCsv(logFile, line);
        }
    }
}