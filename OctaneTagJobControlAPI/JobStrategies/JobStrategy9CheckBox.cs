using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;
using Org.LLRP.LTK.LLRPV1.Impinj;

namespace OctaneTagWritingTest.JobStrategies
{
    /// <summary> 
    /// Single-reader “CheckBox” strategy. 
    /// This strategy uses a single reader configured with four antennas. 
    /// It reads tags for a configurable period, then confirms the number of tags, 
    /// generates a new EPC using a fixed header ("E7") concatenated with a 12-digit SKU (provided by the user) 
    /// and the last 10 hexadecimal digits of the tag’s TID – forming a 24-digit EPC. 
    /// The writing phase runs until all collected tags are written or until a write timeout is reached. 
    /// At the end, it verifies each tag’s new EPC and reports the results. 
    /// </summary> 
    public class JobStrategy9CheckBox : BaseTestStrategy
    {
        private ImpinjReader writerReader;
        // Duration for tag collection (in seconds)
        private const int ReadDurationSeconds = 10;
        // Overall timeout for write operations (in seconds)
        private const int WriteTimeoutSeconds = 20;
        // Duration (in ms) for verification read phase.
        private const int VerificationDurationMs = 5000; 
        // The SKU must contain exactly 12 digits.
        private readonly string sku;
        // Thread-safe dictionary to track for each tag its initial (original) EPC and current verified EPC.
        private readonly ConcurrentDictionary<string, (string OriginalEpc, string VerifiedEpc)> tagData
            = new ConcurrentDictionary<string, (string, string)>();
        // Cumulative count of successfully verified tags
        private int successCount = 0;
        // Dictionary to capture full Tag objects for later processing.
        private readonly ConcurrentDictionary<string, Tag> collectedTags = new ConcurrentDictionary<string, Tag>();
        // Separate dictionary for capturing tags during the verification phase.
        private readonly ConcurrentDictionary<string, Tag> verificationTags = new ConcurrentDictionary<string, Tag>();
        // Flag to indicate if the GPI processing is already running.
        private int gpiProcessingFlag = 0;
        // This flag is also used to stop collecting further tags once the read period has elapsed.
        private bool isCollectingTags = true;
        // Flag to indicate that the verification phase is active.
        private bool isVerificationPhase = false;

        public JobStrategy9CheckBox(string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings, string sku)
            : base(hostname, logFile, readerSettings)
        {
            if (sku.Length != 12)
            {
                throw new ArgumentException("SKU must contain exactly 12 digits.", nameof(sku));
            }
            this.sku = sku;
            // Clean up any previous tag operation state.
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                Console.WriteLine("=== Single Reader CheckBox Test ===");
                Console.WriteLine("GPI events on Port 1 will trigger tag collection, write, and verification. Press 'q' to cancel.");

                // Load any required EPC list.
                EpcListManager.Instance.LoadEpcList("epc_list.txt");

                // Configure the reader and attach event handlers.
                try
                {
                    ConfigureWriterReader();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during writer reader configuration in CheckBox strategy. {ex.Message}");
                    throw;
                }

                // Create CSV header if the log file does not exist.
                if (!File.Exists(logFile))
                    LogToCsv("Timestamp,TID,Expected_EPC,Verified_EPC");


                // Keep the application running until the user cancels.
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
        /// Configures the reader settings and attaches all necessary event handlers.
        /// </summary>
        private void ConfigureWriterReader()
        {
            var writerSettings = GetSettingsForRole("writer");
            if(writerReader == null)
                writerReader = new ImpinjReader();

            if(!writerReader.IsConnected)
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

            // Configure GPI for port 1.
            var gpi = settingsToApply.Gpis.GetGpi(1);
            gpi.IsEnabled = true;
            gpi.DebounceInMs = 50;

            // Set GPI triggers for starting and stopping the operation.
            settingsToApply.AutoStart.Mode = AutoStartMode.GpiTrigger;
            settingsToApply.AutoStart.GpiPortNumber = 1;
            settingsToApply.AutoStart.GpiLevel = true;
            settingsToApply.AutoStop.Mode = AutoStopMode.GpiTrigger;
            settingsToApply.AutoStop.GpiPortNumber = 1;
            settingsToApply.AutoStop.GpiLevel = false;

            // Attach event handlers, including our specialized GPI event handler.
            writerReader.GpiChanged += OnGpiEvent;
            writerReader.TagsReported += OnTagsReported;

            // Enable low latency reporting.
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
                // Use Interlocked.CompareExchange to ensure only one processing instance runs.
                if (Interlocked.CompareExchange(ref gpiProcessingFlag, 1, 0) == 0)
                {
                    Console.WriteLine("GPI Port 1 is TRUE - initiating tag collection and processing.");
                    // Begin tag collection.
                    bool collectionConfirmed = await WaitForReadTagsAsync();
                    if (collectionConfirmed)
                    {
                        // Proceed to execute write/verify operations once collection is done.
                        await EncodeReadTagsAsync();
                        // After writing, start the verification phase.
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
                // When GPI state becomes false, reset the processing flag.
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
            // End tag collection so that no new tags are accepted.
            isCollectingTags = false;

            Console.WriteLine("Tag collection ended. Total tags collected: {0}. Confirm? (y/n)", tagData.Count);
            string confirmation = Console.ReadLine();
            if (!confirmation.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Operation canceled by user.");
                //writerReader.Disconnect();
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
            Console.WriteLine("Starting write phase...");
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
                string newEpc = GenerateNewEpc(sku, tid);
                TagOpController.Instance.RecordExpectedEpc(tid, newEpc);

                // Trigger the write operation using the writer reader.
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
                // Optionally, delay briefly between processing tags.
                await Task.Delay(100, cancellationToken);

                // Record the tag data.
                tagData.AddOrUpdate(tid, (originalEpc, newEpc), (key, old) => (originalEpc, newEpc));
            }
            globalWriteTimer.Stop();
        }

        /// <summary>
        /// Generates a new EPC based on a fixed header ("E7"), the 12-digit SKU, and the last 10 hex digits of the TID.
        /// </summary>
        private string GenerateNewEpc(string sku, string tid)
        {
            string tidSuffix = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
            return "E7" + sku + tidSuffix;
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
            // Prepare for verification.
            verificationTags.Clear();
            isVerificationPhase = true;
            // Wait for the verification period.
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
                if (tagData.TryGetValue(tid, out var expected))
                {
                    if (string.Equals(reportedEpc, expected.VerifiedEpc, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(reportedEpc, expected.VerifiedEpc, StringComparison.Ordinal))
                    {
                        verifiedCount++;
                        Console.WriteLine("Verification SUCCESS: TID {0} reported EPC {1}", tid, reportedEpc);
                        var status = "Success";

                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tid},{reportedEpc},{expected.VerifiedEpc}");
                        TagOpController.Instance.RecordResult(tid, status, true);
                    }
                    else
                    {
                        Console.WriteLine("Verification FAILURE: TID {0} expected EPC {1} but got {2}", tid, expected.VerifiedEpc, reportedEpc);
                        var status = "Failure";

                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tid},{reportedEpc},{expected.VerifiedEpc}");
                        TagOpController.Instance.RecordResult(tid, status, false);
                    }
                }
                else
                {
                    Console.WriteLine("Verification: No expected EPC recorded for TID {0}", tid);
                }
            }
            Console.WriteLine("Verification complete: {0} / {1} tags verified successfully.", verifiedCount, tagData.Count);
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
                //if (writerReader != null)
                //{
                //    //writerReader.GpiChanged -= OnGpiEvent;
                //    // writerReader.TagsReported -= OnTagsReported;
                //    //writerReader.Stop();
                //    //writerReader.Disconnect();
                //}
                // Clear tag collections.
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

        private void LogSuccessCount(object state)
        {
            try
            {
                int successCount = TagOpController.Instance.GetSuccessCount();
                int totalReadCount = TagOpController.Instance.GetTotalReadCount();
                Console.WriteLine($"!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine($"!!!!!!!!!!!!!!!!!!!!!!!!!! Total Read [{totalReadCount}] Success count: [{successCount}] !!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine($"!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                try
                {
                    Console.Title = $"Serializer: {totalReadCount} - {successCount}";

                }
                catch (Exception)
                {


                }
            }
            catch (Exception)
            {
            }

        }
    }

}


