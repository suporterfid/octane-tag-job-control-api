using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagJobControlAPI.Strategies.Base.Configuration;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagWritingTest.Helpers;
using Org.LLRP.LTK.LLRPV1.Impinj;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagJobControlAPI.Models;

namespace OctaneTagJobControlAPI.Strategies
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

        // GPI/GPO configuration
        private bool enableGpiTrigger = false;
        private ushort gpiPort = 1;
        private bool gpiTriggerState = true;
        private bool enableGpoOutput = false;
        private ushort gpoPort = 1;
        private int gpoVerificationTimeoutMs = 1000;

        // GPI event tracking
        private ConcurrentDictionary<long, Stopwatch> gpiEventTimers = new();
        private ConcurrentDictionary<long, bool> gpiEventVerified = new();
        private long lastGpiEventId = 0;

        public MultiReaderEnduranceStrategy(
            string detectorHostname,
            string writerHostname,
            string verifierHostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings,
            IServiceProvider serviceProvider = null)
            : base(detectorHostname, writerHostname, verifierHostname, logFile, readerSettings, serviceProvider)
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

                // Extract configuration settings from parameters
                ExtractConfigurationSettings();


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

                // Register GPI event handler if enabled
                if (enableGpiTrigger)
                {
                    verifierReader.GpiChanged += OnGpiChanged;
                    Console.WriteLine($"GPI trigger enabled on port {gpiPort} with trigger state {gpiTriggerState}");
                }

                // Start readers
                detectorReader.Start();
                writerReader.Start();
                verifierReader.Start();

                // Update status
                status.CurrentOperation = "Running";

                // Create CSV header if needed
                if (!File.Exists(logFile))
                {
                    LogToCsv("Timestamp,TID,Previous_EPC,Expected_EPC,Verified_EPC,WriteTime_ms,VerifyTime_ms,Result,LockStatus,LockTime_ms,CycleCount,RSSI,AntennaPort,GpiTriggered,VerificationTimeMs");
                }

                // Initialize success count timer
                successCountTimer = new Timer(LogSuccessCount, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                    status.RunTime = runTimer.Elapsed;

                    // Check for GPI events that timed out without tag detection
                    CheckGpiTimeouts();
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

        private void ExtractConfigurationSettings()
        {
            try
            {
                // Get the writer settings to extract parameters
                var writerSettings = GetSettingsForRole("writer");

                // Extract lock/permalock settings
                if (settings.ContainsKey("writer") && settings["writer"] != null)
                {
                    var parameters = settings["writer"].Parameters;
                    if (parameters != null)
                    {
                        // Lock/Permalock settings
                        if (parameters.TryGetValue("enableLock", out var lockStr))
                            enableLock = bool.TryParse(lockStr, out bool lockVal) ? lockVal : false;

                        if (parameters.TryGetValue("enablePermalock", out var permalockStr))
                            enablePermalock = bool.TryParse(permalockStr, out bool permalock) ? permalock : false;

                        // GPI/GPO settings
                        if (parameters.TryGetValue("enableGpiTrigger", out var gpiTriggerStr))
                            enableGpiTrigger = bool.TryParse(gpiTriggerStr, out bool gpiTrigger) ? gpiTrigger : false;

                        if (parameters.TryGetValue("gpiPort", out var gpiPortStr))
                            gpiPort = int.TryParse(gpiPortStr, out int port) ? port : 1;

                        if (parameters.TryGetValue("gpiTriggerState", out var gpiStateStr))
                            gpiTriggerState = bool.TryParse(gpiStateStr, out bool state) ? state : true;

                        if (parameters.TryGetValue("enableGpoOutput", out var gpoOutputStr))
                            enableGpoOutput = bool.TryParse(gpoOutputStr, out bool gpoOutput) ? gpoOutput : false;

                        if (parameters.TryGetValue("gpoPort", out var gpoPortStr))
                            gpoPort = int.TryParse(gpoPortStr, out int gpoPortVal) ? gpoPortVal : 1;

                        if (parameters.TryGetValue("gpoVerificationTimeoutMs", out var timeoutStr))
                            gpoVerificationTimeoutMs = int.TryParse(timeoutStr, out int timeout) ? timeout : 1000;
                    }
                }

                Console.WriteLine($"Configuration: Lock={enableLock}, Permalock={enablePermalock}, " +
                                  $"GpiTrigger={enableGpiTrigger} (Port {gpiPort}, State {gpiTriggerState}), " +
                                  $"GpoOutput={enableGpoOutput} (Port {gpoPort}, Timeout {gpoVerificationTimeoutMs}ms)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting configuration settings: {ex.Message}");
                // Default to safe values
                enableLock = false;
                enablePermalock = false;
                enableGpiTrigger = false;
                enableGpoOutput = false;
            }
        }

        protected override Settings ConfigureVerifierReader()
        {
            Settings settings = base.ConfigureVerifierReader();

            if (enableGpiTrigger)
            {
                try
                {
                    // Enable the specified GPI port
                    settings.Gpis.GetGpi(gpiPort).IsEnabled = true;

                    // Apply the settings to the reader
                    verifierReader.ApplySettings(settings);

                    Console.WriteLine($"Configured GPI port {gpiPort} on verifier reader");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error configuring GPI on verifier reader: {ex.Message}");
                }
            }

            return settings;
        }

        // Handle GPI events from the verifier reader
        private void OnGpiChanged(ImpinjReader reader, GpiEvent e)
        {
            // Only process if it's the configured port and state
            if (e.PortNumber == gpiPort && e.State == gpiTriggerState)
            {
                long eventId = Interlocked.Increment(ref lastGpiEventId);
                Console.WriteLine($"GPI event detected on port {e.PortNumber}, State: {e.State}, Event ID: {eventId}");

                // Create timer for this GPI event
                var timer = new Stopwatch();
                timer.Start();
                gpiEventTimers[eventId] = timer;
                gpiEventVerified[eventId] = false;

                // Log the GPI event
                LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},N/A,N/A,N/A,N/A,0,0,GPI_Triggered,None,0,0,0,{e.PortNumber},True,0");

                // Optionally trigger GPO immediately (will be reset if no tag found)
                if (enableGpoOutput)
                {
                    try
                    {
                        // Set the output
                        reader.SetGpo(gpoPort, true);
                        Console.WriteLine($"GPO {gpoPort} activated due to GPI event");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error setting GPO {gpoPort}: {ex.Message}");
                    }
                }
            }
        }

        // Check for GPI events that have timed out without verification
        private void CheckGpiTimeouts()
        {
            if (!enableGpiTrigger) return;

            foreach (var entry in gpiEventTimers.ToArray())
            {
                long eventId = entry.Key;
                Stopwatch timer = entry.Value;

                // Skip events that have been verified
                if (gpiEventVerified.TryGetValue(eventId, out bool verified) && verified)
                    continue;

                // Check if timeout exceeded
                if (timer.ElapsedMilliseconds > gpoVerificationTimeoutMs)
                {
                    Console.WriteLine($"!!! GPI event {eventId} timed out after {timer.ElapsedMilliseconds}ms without tag detection !!!");

                    // Log the error
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},N/A,N/A,N/A,N/A,0,0,Missing_Tag,None,0,0,0,{gpiPort},True,{timer.ElapsedMilliseconds}");

                    // Trigger error GPO output if enabled
                    if (enableGpoOutput)
                    {
                        try
                        {
                            // Set the output to indicate error (toggle)
                            verifierReader.SetGpo(gpoPort, false);
                            Thread.Sleep(200);
                            verifierReader.SetGpo(gpoPort, true);
                            Thread.Sleep(200);
                            verifierReader.SetGpo(gpoPort, false);
                            Console.WriteLine($"GPO {gpoPort} error signal sent (toggled)");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error setting GPO {gpoPort}: {ex.Message}");
                        }
                    }

                    // Remove from tracking
                    gpiEventTimers.TryRemove(eventId, out _);
                    gpiEventVerified.TryRemove(eventId, out _);
                }
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
        /// Also handles GPI-triggered verification if enabled.
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

                // Handle GPI verification if enabled
                if (enableGpiTrigger && gpiEventTimers.Count > 0)
                {
                    // Find the most recent unverified GPI event
                    var unverifiedEvents = gpiEventTimers.Where(e =>
                        !gpiEventVerified.TryGetValue(e.Key, out bool verified) || !verified)
                        .OrderByDescending(e => e.Key)
                        .ToList();

                    if (unverifiedEvents.Any())
                    {
                        var recentEvent = unverifiedEvents.First();
                        long eventId = recentEvent.Key;
                        Stopwatch timer = recentEvent.Value;

                        Console.WriteLine($"Tag detected for GPI event {eventId} after {timer.ElapsedMilliseconds}ms: TID={tidHex}, EPC={epcHex}");

                        // Mark this event as verified
                        gpiEventVerified[eventId] = true;

                        // Set GPO to success if enabled
                        if (enableGpoOutput)
                        {
                            try
                            {
                                if (success)
                                {
                                    // Success signal (steady on)
                                    verifierReader.SetGpo(gpoPort, true);
                                    Console.WriteLine($"GPO {gpoPort} success signal sent (ON)");
                                }
                                else
                                {
                                    // Wrong EPC signal (double pulse)
                                    verifierReader.SetGpo(gpoPort, false);
                                    Thread.Sleep(100);
                                    verifierReader.SetGpo(gpoPort, true);
                                    Thread.Sleep(100);
                                    verifierReader.SetGpo(gpoPort, false);
                                    Console.WriteLine($"GPO {gpoPort} wrong EPC signal sent (double pulse)");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error setting GPO {gpoPort}: {ex.Message}");
                            }
                        }

                        // Log with GPI information
                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},{epcHex},0,{timer.ElapsedMilliseconds},{writeStatus},None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},True,{timer.ElapsedMilliseconds}");

                        // Remove from tracking
                        gpiEventTimers.TryRemove(eventId, out _);
                    }
                }

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

                    // Check if this read was part of a GPI-triggered event
                    bool isGpiTriggered = false;
                    long matchingEventId = 0;
                    long gpiVerificationTime = 0;

                    if (enableGpiTrigger)
                    {
                        // Find if this tag verification corresponds to a recent GPI event
                        var unverifiedEvents = gpiEventTimers.Where(e =>
                            !gpiEventVerified.TryGetValue(e.Key, out bool verified) || !verified)
                            .OrderByDescending(e => e.Key)
                            .ToList();

                        if (unverifiedEvents.Any())
                        {
                            var recentEvent = unverifiedEvents.First();
                            matchingEventId = recentEvent.Key;
                            gpiVerificationTime = recentEvent.Value.ElapsedMilliseconds;

                            // Mark as GPI triggered and verified
                            isGpiTriggered = true;
                            gpiEventVerified[matchingEventId] = true;

                            Console.WriteLine($"Tag read operation completed for GPI event {matchingEventId}: TID={tidHex}, successful={success}");

                            // Set GPO signal based on verification result if enabled
                            if (enableGpoOutput)
                            {
                                try
                                {
                                    if (success)
                                    {
                                        // Success signal (steady on for 1 second)
                                        verifierReader.SetGpo(gpoPort, true);
                                        Console.WriteLine($"GPO {gpoPort} set to ON (success) for GPI event {matchingEventId}");

                                        // Schedule GPO reset after 1 second
                                        new Timer(state => {
                                            try
                                            {
                                                verifierReader.SetGpo(gpoPort, false);
                                                Console.WriteLine($"GPO {gpoPort} reset after success signal");
                                            }
                                            catch (Exception) { /* Ignore timer errors */ }
                                        }, null, 1000, Timeout.Infinite);
                                    }
                                    else
                                    {
                                        // EPC mismatch signal (triple pulse)
                                        verifierReader.SetGpo(gpoPort, true);
                                        Thread.Sleep(100);
                                        verifierReader.SetGpo(gpoPort, false);
                                        Thread.Sleep(100);
                                        verifierReader.SetGpo(gpoPort, true);
                                        Thread.Sleep(100);
                                        verifierReader.SetGpo(gpoPort, false);
                                        Thread.Sleep(100);
                                        verifierReader.SetGpo(gpoPort, true);
                                        Thread.Sleep(100);
                                        verifierReader.SetGpo(gpoPort, false);
                                        Console.WriteLine($"GPO {gpoPort} triple pulse (EPC mismatch) for GPI event {matchingEventId}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error setting GPO {gpoPort}: {ex.Message}");
                                }
                            }

                            // Remove from tracking
                            gpiEventTimers.TryRemove(matchingEventId, out _);
                        }
                    }

                    // Log tag read/write result, including GPI information if applicable
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{status},{lockStatus},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},{readResult.Tag.PeakRssiInDbm},{readResult.Tag.AntennaPortNumber},{isGpiTriggered},{gpiVerificationTime}");
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
                        catch (Exception ex)
                        {
                            // Log exception but continue processing
                            Console.WriteLine($"Error triggering write after verification failure: {ex.Message}");
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
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{currentEpc},{expectedEpc},{currentEpc},{swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},{swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},Success,{(success ? lockStatus : lockStatus + "Failed")},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},{lockResult.Tag.PeakRssiInDbm},{lockResult.Tag.AntennaPortNumber},False,0");
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
                int gpiEventsTotal = (int)lastGpiEventId;
                int gpiEventsVerified = gpiEventVerified.Count(kv => kv.Value);
                int gpiEventsPending = gpiEventTimers.Count;

                lock (status)
                {
                    status.TotalTagsProcessed = totalReadCount;
                    status.SuccessCount = successCount;
                    status.FailureCount = totalReadCount - successCount;
                    status.ProgressPercentage = totalReadCount > 0
                        ? Math.Min(100, (double)successCount / totalReadCount * 100)
                        : 0;

                    // Add lock and GPI status to metrics
                    status.Metrics["LockedTags"] = lockedCount;
                    status.Metrics["LockEnabled"] = enableLock;
                    status.Metrics["PermalockEnabled"] = enablePermalock;
                    status.Metrics["GpiEventsTotal"] = gpiEventsTotal;
                    status.Metrics["GpiEventsVerified"] = gpiEventsVerified;
                    status.Metrics["GpiEventsMissingTag"] = gpiEventsTotal - gpiEventsVerified;
                    status.Metrics["GpiEventsPending"] = gpiEventsPending;
                }

                Console.WriteLine($"Total Read [{totalReadCount}] Success count: [{successCount}] Locked/Permalocked: [{lockedCount}] GPI Events: {gpiEventsTotal} (Verified: {gpiEventsVerified}, Missing: {gpiEventsTotal - gpiEventsVerified}, Pending: {gpiEventsPending})");
            }
            catch (Exception ex)
            {
                // Ignore timer exceptions
                Console.WriteLine($"Error in LogSuccessCount: {ex.Message}");
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
                        { "GpiEnabled", enableGpiTrigger },
                        { "GpiPort", gpiPort },
                        { "GpiTriggerState", gpiTriggerState },
                        { "GpoEnabled", enableGpoOutput },
                        { "GpoPort", gpoPort },
                        { "GpiEventsTotal", lastGpiEventId },
                        { "GpiEventsVerified", gpiEventVerified.Count(kv => kv.Value) },
                        { "GpiEventsMissingTag", lastGpiEventId - gpiEventVerified.Count(kv => kv.Value) },
                        { "GpiEventsPending", gpiEventTimers.Count },
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
                Description = "Performs endurance testing using multiple readers for detection, writing, and verification with optional locking and GPI trigger support",
                Category = "Advanced Testing",
                ConfigurationType = typeof(EnduranceTestConfiguration),
                Capabilities = StrategyCapability.Reading | StrategyCapability.Writing |
                    StrategyCapability.Verification | StrategyCapability.MultiReader |
                    StrategyCapability.MultiAntenna | StrategyCapability.Permalock,
                RequiresMultipleReaders = true
            };
        }

        public override void Dispose()
        {
            // Turn off any GPO outputs that might still be on
            if (enableGpoOutput && verifierReader != null)
            {
                try
                {
                    verifierReader.SetGpo(gpoPort, false);
                }
                catch (Exception)
                {
                    // Ignore errors during cleanup
                }
            }

            // Dispose of any timers
            foreach (var timer in gpiEventTimers.Values)
            {
                timer.Stop();
            }

            // Call the base class dispose method
            base.Dispose();
        }

    }
}