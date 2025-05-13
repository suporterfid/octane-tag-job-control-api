using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagJobControlAPI.JobStrategies.Base.Configuration;
using OctaneTagJobControlAPI.JobStrategies.Base;
using OctaneTagWritingTest.Helpers;
using Org.LLRP.LTK.LLRPV1.Impinj;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagJobControlAPI.Models;

namespace OctaneTagJobControlAPI.JobStrategies
{
    /// <summary>
    /// Dual-reader endurance strategy.
    /// The first reader (writerReader) reads tags, generates or retrieves a new EPC for the read TID,
    /// and sends a write operation. The second reader (verifierReader) monitors tag reads,
    /// comparing each tag's EPC to its expected value; if a mismatch is found, it triggers a re-write
    /// using the expected EPC.
    /// </summary>
    [StrategyDescription(
    "Performs endurance testing using multiple readers for detection, writing, and verification with optional locking",
    "Advanced Testing",
    StrategyCapability.Reading | StrategyCapability.Writing | StrategyCapability.Verification |
    StrategyCapability.MultiReader | StrategyCapability.MultiAntenna | StrategyCapability.Permalock)]
    public class MultiReaderEnduranceStrategy : MultiReaderStrategyBase
    {
        private const int MaxCycles = 10000;
        private readonly ConcurrentDictionary<string, int> cycleCount = new();
        private readonly ConcurrentDictionary<string, Stopwatch> swWriteTimers = new();
        private readonly ConcurrentDictionary<string, Stopwatch> swVerifyTimers = new();
        private readonly ConcurrentDictionary<string, Stopwatch> swLockTimers = new();
        private Timer successCountTimer;
        private JobExecutionStatus status = new();
        private readonly Stopwatch runTimer = new();

        // Lock/Permalock configuration
        private bool enableLock = false;
        private bool enablePermalock = false;
        private readonly ConcurrentDictionary<string, bool> lockedTags = new();

        public MultiReaderEnduranceStrategy(
            string detectorHostname,
            string writerHostname,
            string verifierHostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings)
            : base(detectorHostname, writerHostname, verifierHostname, logFile, readerSettings)
        {
            status.CurrentOperation = "Initialized";
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                status.CurrentOperation = "Starting";
                runTimer.Start();

                Console.WriteLine("=== Multiple Reader Endurance Test ===");
                Console.WriteLine("Press 'q' to stop the test and return to menu.");

                // Extract lock/permalock configuration from settings if available
                // Try to get the lock and permalock settings - this should be adapted to match how parameters are passed
                try
                {
                    // In a real implementation, this would extract configuration from parameters
                    // For now, we'll check if any writer settings has parameters for lock/permalock
                    var writerSettings = GetSettingsForRole("writer");

                    // Example of checking for lock/permalock in configuration parameters
                    // This is a placeholder implementation that would need to be adjusted
                    // based on how parameters are actually stored

                    // In a real scenario, these would come from WriteStrategyConfiguration parameters
                    enableLock = false; // Default to false
                    enablePermalock = false; // Default to false

                    Console.WriteLine($"Lock setting: {enableLock}, Permalock setting: {enablePermalock}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accessing lock configuration: {ex.Message}");
                    // Default to false for both if there's any error
                    enableLock = false;
                    enablePermalock = false;
                }

                // Configure readers.
                try
                {
                    ConfigureDetectorReader();
                    ConfigureWriterReader();
                    ConfigureVerifierReader();
                }
                catch (Exception ex)
                {
                    status.CurrentOperation = "Configuration Error";
                    Console.WriteLine($"Error configuring readers: {ex.Message}");
                    throw;
                }

                // Register event handlers
                detectorReader.TagsReported += OnTagsReportedDetector;
                writerReader.TagsReported += OnTagsReportedWriter;
                writerReader.TagOpComplete += OnTagOpComplete;
                verifierReader.TagsReported += OnTagsReportedVerifier;
                verifierReader.TagOpComplete += OnTagOpComplete;

                // Start readers
                detectorReader.Start();
                writerReader.Start();
                verifierReader.Start();

                // Update status
                status.CurrentOperation = "Running";

                // Create CSV header if needed
                if (!File.Exists(logFile))
                {
                    // Update header to include lock status
                    LogToCsv("Timestamp,TID,Previous_EPC,Expected_EPC,Verified_EPC,WriteTime_ms,VerifyTime_ms,Result,LockStatus,LockTime_ms,CycleCount,RSSI,AntennaPort");
                }

                // Initialize success count timer
                successCountTimer = new Timer(LogSuccessCount, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                    status.RunTime = runTimer.Elapsed;
                }

                status.CurrentOperation = "Stopping";
                Console.WriteLine("\nStopping test...");
            }
            catch (Exception ex)
            {
                status.CurrentOperation = "Error";
                Console.WriteLine("Error in multi-reader test: " + ex.Message);
            }
            finally
            {
                runTimer.Stop();
                successCountTimer?.Dispose();
                CleanupReaders();
            }
        }

        private void OnTagsReportedDetector(ImpinjReader sender, TagReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                var epcHex = tag.Epc?.ToHexString() ?? string.Empty;
                if (string.IsNullOrEmpty(tidHex) || TagOpController.Instance.IsTidProcessed(tidHex))
                    continue;

                // Here, simply record and log the detection.
                Console.WriteLine($"Detector: New tag detected. TID: {tidHex}, Current EPC: {epcHex}");
                // Generate a new EPC (if one is not already recorded) using the detector logic.
                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                if (string.IsNullOrEmpty(expectedEpc))
                {
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"Detector: Assigned new EPC for TID {tidHex}: {expectedEpc}");

                    // Trigger the write operation using the writer reader.
                    TagOpController.Instance.TriggerWriteAndVerify(
                        tag,
                        expectedEpc,
                        writerReader,
                        cancellationToken,
                        swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                        newAccessPassword,
                        true,
                        1,
                        true,
                        0);
                }
                // (Optionally, you might also update any UI or log this detection.)
            }
        }

        /// <summary>
        /// Event handler for tag reports from the writer reader.
        /// Captures the EPC and TID, generates or retrieves a new EPC for the TID, and sends the write operation.
        /// </summary>
        private void OnTagsReportedWriter(ImpinjReader sender, TagReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                var epcHex = tag.Epc?.ToHexString() ?? string.Empty;
                if (string.IsNullOrEmpty(tidHex) || TagOpController.Instance.IsTidProcessed(tidHex))
                    continue;

                cycleCount.TryAdd(tidHex, 0);

                if (cycleCount[tidHex] >= MaxCycles)
                {
                    Console.WriteLine($"Max cycles reached for TID {tidHex}, skipping further processing.");
                    continue;
                }

                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                // If no expected EPC exists, generate one using the writer logic.
                if (string.IsNullOrEmpty(expectedEpc))
                {
                    Console.WriteLine($">>>>>>>>>>New target TID found: {tidHex} Chip {TagOpController.Instance.GetChipModel(tag)}");
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($">>>>>>>>>>New tag found. TID: {tidHex}. Assigning new EPC: {epcHex} -> {expectedEpc}");
                }


                if (!expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase))
                {
                    // Trigger the write operation using the writer reader.
                    TagOpController.Instance.TriggerWriteAndVerify(
                        tag,
                        expectedEpc,
                        sender,
                        cancellationToken,
                        swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                        newAccessPassword,
                        true,
                        1,
                        true,
                        3);

                    //TagOpController.Instance.TriggerPartialWriteAndVerify(
                    //    tag,
                    //    expectedEpc,
                    //    writerReader,
                    //    cancellationToken,
                    //    swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                    //    newAccessPassword,
                    //    true,
                    //    14,
                    //    1,
                    //    true,
                    //    3);
                }
                else
                {
                    if (expectedEpc != null && expectedEpc.Equals(epcHex, StringComparison.OrdinalIgnoreCase))
                    {
                        TagOpController.Instance.HandleVerifiedTag(tag, tidHex, expectedEpc, swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()), cycleCount, tag, TagOpController.Instance.GetChipModel(tag), logFile);
                        //Console.WriteLine($"TID {tidHex} verified successfully on writer reader. Current EPC: {epcHex}");
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Event handler for tag reports from the verifier reader.
        /// Compares the tag's EPC with the expected EPC; if they do not match, triggers a write operation retry using the expected EPC.
        /// If they match and locking is enabled, may perform a lock or permalock operation.
        /// </summary>
        private void OnTagsReportedVerifier(ImpinjReader sender, TagReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                if (string.IsNullOrEmpty(tidHex))
                    continue;

                var epcHex = tag.Epc.ToHexString() ?? string.Empty;
                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                // If no expected EPC exists, generate one using the writer logic.
                if (string.IsNullOrEmpty(expectedEpc))
                {
                    Console.WriteLine($"OnTagsReportedVerifier>>>>>>>>>>TID not found. Considering re-write for target TID found: {tidHex} Chip {TagOpController.Instance.GetChipModel(tag)}");
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"OnTagsReportedVerifier>>>>>>>>>> TID: {tidHex}. Assigning EPC: {epcHex} -> {expectedEpc}");
                }

                bool success = expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase);
                var writeStatus = success ? "Success" : "Failure";
                Console.WriteLine(".........................................");
                Console.WriteLine($"OnTagsReportedVerifier - TID {tidHex} - current EPC: {epcHex} Expected EPC: {expectedEpc} Operation Status [{writeStatus}]");
                Console.WriteLine(".........................................");

                if (success)
                {
                    // Check if the tag has already been locked
                    bool alreadyLocked = lockedTags.ContainsKey(tidHex);

                    // If the tag is successfully verified and lock/permalock is enabled but we haven't locked it yet
                    if ((enableLock || enablePermalock) && !alreadyLocked)
                    {
                        // Start timing the lock operation
                        var lockTimer = swLockTimers.GetOrAdd(tidHex, _ => new Stopwatch());
                        lockTimer.Restart();

                        // Perform either permalock or standard lock operation
                        if (enablePermalock)
                        {
                            Console.WriteLine($"Permalocking tag with TID {tidHex}");
                            TagOpController.Instance.PermaLockTag(tag, newAccessPassword, sender);
                        }
                        else if (enableLock)
                        {
                            Console.WriteLine($"Locking tag with TID {tidHex}");
                            TagOpController.Instance.LockTag(tag, newAccessPassword, sender);
                        }

                        // Mark this tag as having had a lock operation triggered
                        lockedTags.TryAdd(tidHex, true);
                    }
                    else
                    {
                        // If we're not locking or the tag is already locked, just record the success
                        TagOpController.Instance.RecordResult(tidHex, writeStatus, success);
                        Console.WriteLine($"OnTagsReportedVerifier - TID {tidHex} verified successfully on verifier reader. Current EPC: {epcHex} - Written tags registered {TagOpController.Instance.GetSuccessCount()} (TIDs processed)");
                    }
                }
                else if (!string.IsNullOrEmpty(expectedEpc))
                {
                    if (!expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase))
                    {
                        Console.WriteLine($"Verification mismatch for TID {tidHex}: expected {expectedEpc}, read {epcHex}. Retrying write operation using expected EPC.");
                        // Retry writing using the expected EPC (without generating a new one) via the verifier reader.
                        TagOpController.Instance.TriggerWriteAndVerify(
                            tag,
                            expectedEpc,
                            sender,
                            cancellationToken,
                            swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                            newAccessPassword,
                            true,
                            1,
                            false,
                            3);
                    }
                    else
                    {
                        Console.WriteLine($"OnTagsReportedVerifier - TID {tidHex} verified successfully on verifier reader. Current EPC: {epcHex} - Written tags registered {TagOpController.Instance.GetSuccessCount()} (TIDs processed)");
                    }
                }
            }
        }

        /// <summary>
        /// Common event handler for tag operation completions from both readers.
        /// Processes write, read, and lock operations, logs the result, and updates the cycle count.
        /// </summary>
        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (TagOpResult result in report)
            {
                var tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";

                if (result is TagWriteOpResult writeResult)
                {
                    swWriteTimers[tidHex].Stop();

                    var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    var verifiedEpc = writeResult.Tag.Epc?.ToHexString() ?? "N/A";
                    bool success = !string.IsNullOrEmpty(expectedEpc) && expectedEpc.Equals(verifiedEpc, StringComparison.InvariantCultureIgnoreCase);
                    var writeStatus = success ? "Success" : "Failure";
                    if (success)
                    {
                        TagOpController.Instance.RecordResult(tidHex, writeStatus, success);
                    }
                    else if (writeResult.Result == WriteResultStatus.Success)
                    {
                        Console.WriteLine($"OnTagOpComplete - Write operation succeeded for TID {tidHex} on reader {sender.Name}.");
                        // After a successful write, trigger a verification read on the verifier reader.
                        TagOpController.Instance.TriggerVerificationRead(
                            result.Tag,
                            sender,
                            cancellationToken,
                            swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                            newAccessPassword);
                    }
                    else
                    {
                        Console.WriteLine($"OnTagOpComplete - Write operation failed for TID {tidHex} on reader {sender.Name}.");
                    }
                }
                else if (result is TagReadOpResult readResult)
                {
                    swVerifyTimers[tidHex].Stop();

                    var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    if (string.IsNullOrEmpty(expectedEpc))
                    {
                        expectedEpc = TagOpController.Instance.GetNextEpcForTag(readResult.Tag.Epc.ToHexString(), tidHex);
                    }
                    var verifiedEpc = readResult.Tag.Epc?.ToHexString() ?? "N/A";
                    var success = verifiedEpc.Equals(expectedEpc, StringComparison.InvariantCultureIgnoreCase);
                    var status = success ? "Success" : "Failure";

                    // Get or create a lock timer for this tag
                    var lockTimer = swLockTimers.GetOrAdd(tidHex, _ => new Stopwatch());
                    // Determine lock status based on whether we're locking and if the timer has run
                    string lockStatus = "None";
                    if (enablePermalock && lockedTags.ContainsKey(tidHex))
                        lockStatus = "Permalocked";
                    else if (enableLock && lockedTags.ContainsKey(tidHex))
                        lockStatus = "Locked";

                    // Log tag read/write result, now including lock information
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{status},{lockStatus},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},RSSI,AntennaPort");
                    TagOpController.Instance.RecordResult(tidHex, status, success);

                    Console.WriteLine($"OnTagOpComplete - Verification result for TID {tidHex} on reader {sender.Address}: {status}");

                    cycleCount.AddOrUpdate(tidHex, 1, (key, oldValue) => oldValue + 1);

                    if (!success)
                    {
                        try
                        {
                            TagOpController.Instance.TriggerWriteAndVerify(
                            readResult.Tag,
                            expectedEpc,
                            sender,
                            cancellationToken,
                            swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                            newAccessPassword,
                            true,
                            1,
                            true,
                            3);
                        }
                        catch (Exception)
                        {
                            // Log exception but continue processing
                        }
                    }
                }
                else if (result is TagLockOpResult lockResult)
                {
                    // Handle lock operation result
                    var lockTimer = swLockTimers.GetOrAdd(tidHex, _ => new Stopwatch());
                    lockTimer.Stop();

                    bool success = lockResult.Result == LockResultStatus.Success;
                    string lockStatus = enablePermalock ? "Permalocked" : "Locked";
                    string lockOpStatus = success ? "Success" : "Failure";

                    Console.WriteLine($"OnTagOpComplete - {lockStatus} operation for TID {tidHex} on reader {sender.Address}: {lockOpStatus} in {lockTimer.ElapsedMilliseconds}ms");

                    // If lock failed but EPC is still correct, we've still completed the main operation
                    var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    var currentEpc = lockResult.Tag.Epc?.ToHexString() ?? "N/A";

                    bool epcCorrect = !string.IsNullOrEmpty(expectedEpc) &&
                                      expectedEpc.Equals(currentEpc, StringComparison.InvariantCultureIgnoreCase);

                    // Overall success is based on the EPC being correct, even if lock fails
                    // This approach prioritizes getting the EPC right, with locking as a "nice to have"
                    if (epcCorrect)
                    {
                        TagOpController.Instance.RecordResult(tidHex, "Success", true);
                    }

                    // Log the lock operation
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{currentEpc},{expectedEpc},{currentEpc},{swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},{swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},Success,{(success ? lockStatus : lockStatus + "Failed")},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},RSSI,AntennaPort");
                }
            }
        }

        private void LogSuccessCount(object state)
        {
            try
            {
                int successCount = TagOpController.Instance.GetSuccessCount();
                int totalReadCount = TagOpController.Instance.GetTotalReadCount();
                int lockedCount = lockedTags.Count;

                lock (status)
                {
                    status.TotalTagsProcessed = totalReadCount;
                    status.SuccessCount = successCount;
                    status.FailureCount = totalReadCount - successCount;
                    status.ProgressPercentage = totalReadCount > 0
                        ? Math.Min(100, (double)successCount / totalReadCount * 100)
                        : 0;

                    // Add lock status to metrics
                    status.Metrics["LockedTags"] = lockedCount;
                    status.Metrics["LockEnabled"] = enableLock;
                    status.Metrics["PermalockEnabled"] = enablePermalock;
                }

                Console.WriteLine($"Total Read [{totalReadCount}] Success count: [{successCount}] Locked/Permalocked: [{lockedCount}]");
            }
            catch (Exception)
            {
                // Ignore timer exceptions
            }
        }

        public override JobExecutionStatus GetStatus()
        {
            lock (status)
            {
                return new JobExecutionStatus
                {
                    TotalTagsProcessed = status.TotalTagsProcessed,
                    SuccessCount = status.SuccessCount,
                    FailureCount = status.FailureCount,
                    ProgressPercentage = status.ProgressPercentage,
                    CurrentOperation = status.CurrentOperation,
                    RunTime = status.RunTime,
                    Metrics = new Dictionary<string, object>
                    {
                        { "CycleCount", cycleCount.Count > 0 ? cycleCount.Values.Average() : 0 },
                        { "MaxCycle", cycleCount.Count > 0 ? cycleCount.Values.Max() : 0 },
                        { "AvgWriteTimeMs", swWriteTimers.Count > 0 ? swWriteTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0 },
                        { "AvgVerifyTimeMs", swVerifyTimers.Count > 0 ? swVerifyTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0 },
                        { "AvgLockTimeMs", swLockTimers.Count > 0 ? swLockTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0 },
                        { "LockedTags", lockedTags.Count },
                        { "LockEnabled", enableLock },
                        { "PermalockEnabled", enablePermalock },
                        { "ElapsedSeconds", runTimer.Elapsed.TotalSeconds }
                    }
                };
            }
        }

        public override StrategyMetadata GetMetadata()
        {
            return new StrategyMetadata
            {
                Name = "MultiReaderEnduranceStrategy",
                Description = "Performs endurance testing using multiple readers for detection, writing, and verification with optional locking",
                Category = "Advanced Testing",
                ConfigurationType = typeof(WriteStrategyConfiguration),
                Capabilities = StrategyCapability.Reading | StrategyCapability.Writing |
                    StrategyCapability.Verification | StrategyCapability.MultiReader |
                    StrategyCapability.MultiAntenna | StrategyCapability.Permalock,
                RequiresMultipleReaders = true
            };
        }
    }
}