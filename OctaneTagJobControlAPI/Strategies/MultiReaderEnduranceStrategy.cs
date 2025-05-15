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
        private ConcurrentDictionary<long, ImpinjReader> gpiEventReaders = new();  // Which reader triggered the event
        private ConcurrentDictionary<long, string> gpiEventReaderRoles = new();    // Reader role (detector, writer, verifier)
        private ConcurrentDictionary<long, bool> gpiEventGpoEnabled = new();       // GPO enabled for this event
        private ConcurrentDictionary<long, ushort> gpiEventGpoPorts = new();          // GPO port for this event
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
                
                // Register GPI event handlers for each reader that has GPI enabled
                if (settings.TryGetValue("verifier", out var verifierSettings) && 
                    verifierSettings.Parameters != null &&
                    verifierSettings.Parameters.TryGetValue("enableGpiTrigger", out var vGpiStr) &&
                    bool.TryParse(vGpiStr, out bool vGpiEnabled) && 
                    vGpiEnabled)
                {
                    verifierReader.GpiChanged += OnGpiChanged;
                    Console.WriteLine($"GPI trigger enabled on verifier reader");
                }
                
                if (settings.TryGetValue("detector", out var detectorSettings) && 
                    detectorSettings.Parameters != null &&
                    detectorSettings.Parameters.TryGetValue("enableGpiTrigger", out var dGpiStr) &&
                    bool.TryParse(dGpiStr, out bool dGpiEnabled) && 
                    dGpiEnabled)
                {
                    detectorReader.GpiChanged += OnGpiChanged;
                    Console.WriteLine($"GPI trigger enabled on detector reader");
                }
                
                if (settings.TryGetValue("writer", out var writerSettings) && 
                    writerSettings.Parameters != null &&
                    writerSettings.Parameters.TryGetValue("enableGpiTrigger", out var wGpiStr) &&
                    bool.TryParse(wGpiStr, out bool wGpiEnabled) && 
                    wGpiEnabled)
                {
                    writerReader.GpiChanged += OnGpiChanged;
                    Console.WriteLine($"GPI trigger enabled on writer reader");
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
                    // Update header to include GPI/GPO status
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

        // Extract configuration settings from reader-specific parameters
        private void ExtractConfigurationSettings()
        {
            try
            {
                // Set defaults
                enableLock = false;
                enablePermalock = false;
                enableGpiTrigger = false;
                gpiPort = 1;
                gpiTriggerState = true;
                enableGpoOutput = false;
                gpoPort = 1;
                gpoVerificationTimeoutMs = 1000;
                
                // Extract lock/permalock settings from writer reader
                if (settings.TryGetValue("writer", out var writerSettings) && writerSettings.Parameters != null)
                {
                    // Extract lock settings
                    if (writerSettings.Parameters.TryGetValue("enableLock", out var lockStr))
                        enableLock = bool.TryParse(lockStr, out bool lockVal) ? lockVal : false;

                    if (writerSettings.Parameters.TryGetValue("enablePermalock", out var permalockStr))
                        enablePermalock = bool.TryParse(permalockStr, out bool permalock) ? permalock : false;
                }
                
                // Extract GPI settings from verifier reader (primary location for GPI/GPO settings)
                if (settings.TryGetValue("verifier", out var verifierSettings) && verifierSettings.Parameters != null)
                {
                    // GPI settings
                    if (verifierSettings.Parameters.TryGetValue("enableGpiTrigger", out var gpiTriggerStr))
                        enableGpiTrigger = bool.TryParse(gpiTriggerStr, out bool gpiTrigger) ? gpiTrigger : false;

                    if (verifierSettings.Parameters.TryGetValue("gpiPort", out var gpiPortStr))
                        gpiPort = ushort.TryParse(gpiPortStr, out ushort port) ? port : (ushort)1;

                    if (verifierSettings.Parameters.TryGetValue("gpiTriggerState", out var gpiStateStr))
                        gpiTriggerState = bool.TryParse(gpiStateStr, out bool state) ? state : true;

                    // GPO settings
                    if (verifierSettings.Parameters.TryGetValue("enableGpoOutput", out var gpoOutputStr))
                        enableGpoOutput = bool.TryParse(gpoOutputStr, out bool gpoOutput) ? gpoOutput : false;

                    if (verifierSettings.Parameters.TryGetValue("gpoPort", out var gpoPortStr))
                        gpoPort = ushort.TryParse(gpoPortStr, out ushort gpoPortVal) ? gpoPortVal : (ushort)1;

                    if (verifierSettings.Parameters.TryGetValue("gpoVerificationTimeoutMs", out var timeoutStr))
                        gpoVerificationTimeoutMs = int.TryParse(timeoutStr, out int timeout) ? timeout : 1000;
                }
                
                // Check if we should use detector reader parameters instead
                // (Advanced feature: we might want different readers to handle different GPI events)
                if (settings.TryGetValue("detector", out var detectorSettings) && detectorSettings.Parameters != null)
                {
                    // Only override if explicitly enabled on detector
                    if (detectorSettings.Parameters.TryGetValue("enableGpiTrigger", out var detGpiTriggerStr) &&
                        bool.TryParse(detGpiTriggerStr, out bool detGpiTrigger) && detGpiTrigger)
                    {
                        // Override with detector settings
                        enableGpiTrigger = true;
                        
                        if (detectorSettings.Parameters.TryGetValue("gpiPort", out var detGpiPortStr))
                            gpiPort = ushort.TryParse(detGpiPortStr, out ushort detPort) ? detPort : gpiPort;
                            
                        if (detectorSettings.Parameters.TryGetValue("gpiTriggerState", out var detGpiStateStr))
                            gpiTriggerState = bool.TryParse(detGpiStateStr, out bool detState) ? detState : gpiTriggerState;
                    }
                    
                    // Only override GPO if explicitly enabled on detector
                    if (detectorSettings.Parameters.TryGetValue("enableGpoOutput", out var detGpoOutputStr) &&
                        bool.TryParse(detGpoOutputStr, out bool detGpoOutput) && detGpoOutput)
                    {
                        // Override with detector settings
                        enableGpoOutput = true;
                        
                        if (detectorSettings.Parameters.TryGetValue("gpoPort", out var detGpoPortStr))
                            gpoPort = ushort.TryParse(detGpoPortStr, out ushort detGpoPort) ? detGpoPort : gpoPort;
                            
                        if (detectorSettings.Parameters.TryGetValue("gpoVerificationTimeoutMs", out var detTimeoutStr))
                            gpoVerificationTimeoutMs = ushort.TryParse(detTimeoutStr, out ushort detTimeout) ? detTimeout : gpoVerificationTimeoutMs;
                    }
                }
                
                // Also check global parameters from EnduranceTestConfiguration if available
                // This would be used if parameters are set at the strategy level rather than reader-specific
                if (_serviceProvider != null)
                {
                    // Try to get EnduranceTestConfiguration from service provider if available
                    var configService = _serviceProvider.GetService(typeof(EnduranceTestConfiguration)) as EnduranceTestConfiguration;
                    if (configService != null)
                    {
                        // Only override if the properties exist in the configuration
                        // This assumes EnduranceTestConfiguration has been updated with these properties
                        
                        // For lock settings, strategy-level settings take precedence
                        enableLock = configService.LockAfterWrite;
                        enablePermalock = configService.PermalockAfterWrite;
                        
                        // For GPI/GPO, only override if not already set by reader-specific parameters
                        if (configService.GetType().GetProperty("EnableGpiTrigger") != null)
                        {
                            var gpiTriggerProp = configService.GetType().GetProperty("EnableGpiTrigger");
                            if (gpiTriggerProp != null && !enableGpiTrigger)
                            {
                                var value = gpiTriggerProp.GetValue(configService);
                                if (value is bool gpiEnabled)
                                    enableGpiTrigger = gpiEnabled;
                            }
                        }
                        
                        // Similar checks for other properties
                        // (Note: This reflection-based approach is just a fallback; directly accessing properties
                        // would be better once EnduranceTestConfiguration is updated)
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

        // Override ConfigureVerifierReader to enable GPI events if needed
        protected override Settings ConfigureVerifierReader()
        {
            Settings settings = base.ConfigureVerifierReader();
            
            // Check if GPI is enabled specifically for the verifier reader
            if (enableGpiTrigger && this.settings.TryGetValue("verifier", out var verifierSettings))
            {
                try
                {
                    // Get the port from verifier-specific settings, if available
                    ushort verifierGpiPort = gpiPort;
                    if (verifierSettings.Parameters != null && 
                        verifierSettings.Parameters.TryGetValue("gpiPort", out var portStr) &&
                        ushort.TryParse(portStr, out ushort port))
                    {
                        verifierGpiPort = port;
                    }
                    
                    // Enable the specified GPI port
                    settings.Gpis.GetGpi(verifierGpiPort).IsEnabled = true;
                    
                    // Apply the settings to the reader
                    verifierReader.ApplySettings(settings);
                    
                    Console.WriteLine($"Configured GPI port {verifierGpiPort} on verifier reader");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error configuring GPI on verifier reader: {ex.Message}");
                }
            }
            
            return settings;
        }

        // Add method to configure detector reader GPI if needed
        protected override Settings ConfigureDetectorReader()
        {
            Settings settings = base.ConfigureDetectorReader();
            
            // Check if GPI is enabled specifically for the detector reader
            if (this.settings.TryGetValue("detector", out var detectorSettings) &&
                detectorSettings.Parameters != null &&
                detectorSettings.Parameters.TryGetValue("enableGpiTrigger", out var gpiEnableStr) &&
                bool.TryParse(gpiEnableStr, out bool gpiEnabled) &&
                gpiEnabled)
            {
                try
                {
                    // Get port from detector-specific settings
                    ushort detectorGpiPort = 1; // Default
                    if (detectorSettings.Parameters.TryGetValue("gpiPort", out var portStr) &&
                        ushort.TryParse(portStr, out ushort port))
                    {
                        detectorGpiPort = port;
                    }
                    
                    // Enable the specified GPI port
                    settings.Gpis.GetGpi(detectorGpiPort).IsEnabled = true;
                    
                    // Apply the settings to the reader
                    detectorReader.ApplySettings(settings);
                    
                    // If detector has GPI enabled, register GPI handler
                    detectorReader.GpiChanged += OnGpiChanged;
                    
                    Console.WriteLine($"Configured GPI port {detectorGpiPort} on detector reader");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error configuring GPI on detector reader: {ex.Message}");
                }
            }
            
            return settings;
        }

        // Add method to configure writer reader GPI if needed
        protected override Settings ConfigureWriterReader()
        {
            Settings settings = base.ConfigureWriterReader();
            
            // Check if GPI is enabled specifically for the writer reader
            if (this.settings.TryGetValue("writer", out var writerSettings) &&
                writerSettings.Parameters != null &&
                writerSettings.Parameters.TryGetValue("enableGpiTrigger", out var gpiEnableStr) &&
                bool.TryParse(gpiEnableStr, out bool gpiEnabled) &&
                gpiEnabled)
            {
                try
                {
                    // Get port from writer-specific settings
                    ushort writerGpiPort = 1; // Default
                    if (writerSettings.Parameters.TryGetValue("gpiPort", out var portStr) &&
                        ushort.TryParse(portStr, out ushort port))
                    {
                        writerGpiPort = port;
                    }
                    
                    // Enable the specified GPI port
                    settings.Gpis.GetGpi(writerGpiPort).IsEnabled = true;
                    
                    // Apply the settings to the reader
                    writerReader.ApplySettings(settings);
                    
                    // If writer has GPI enabled, register GPI handler
                    writerReader.GpiChanged += OnGpiChanged;
                    
                    Console.WriteLine($"Configured GPI port {writerGpiPort} on writer reader");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error configuring GPI on writer reader: {ex.Message}");
                }
            }
            
            return settings;
        }

        // Handle GPI events from any reader
        private void OnGpiChanged(ImpinjReader reader, GpiEvent e)
        {
            // Determine which reader triggered the event
            string readerRole = "unknown";
            int configuredPort = gpiPort;
            bool configuredState = gpiTriggerState;
            
            if (reader == verifierReader)
            {
                readerRole = "verifier";
                
                // Get verifier-specific settings
                if (settings.TryGetValue("verifier", out var verifierSettings) && 
                    verifierSettings.Parameters != null)
                {
                    if (verifierSettings.Parameters.TryGetValue("gpiPort", out var portStr) &&
                        ushort.TryParse(portStr, out ushort port))
                    {
                        configuredPort = port;
                    }
                    
                    if (verifierSettings.Parameters.TryGetValue("gpiTriggerState", out var stateStr) &&
                        bool.TryParse(stateStr, out bool state))
                    {
                        configuredState = state;
                    }
                }
            }
            else if (reader == detectorReader)
            {
                readerRole = "detector";
                
                // Get detector-specific settings
                if (settings.TryGetValue("detector", out var detectorSettings) && 
                    detectorSettings.Parameters != null)
                {
                    if (detectorSettings.Parameters.TryGetValue("gpiPort", out var portStr) &&
                        int.TryParse(portStr, out int port))
                    {
                        configuredPort = port;
                    }
                    
                    if (detectorSettings.Parameters.TryGetValue("gpiTriggerState", out var stateStr) &&
                        bool.TryParse(stateStr, out bool state))
                    {
                        configuredState = state;
                    }
                }
            }
            else if (reader == writerReader)
            {
                readerRole = "writer";
                
                // Get writer-specific settings
                if (settings.TryGetValue("writer", out var writerSettings) && 
                    writerSettings.Parameters != null)
                {
                    if (writerSettings.Parameters.TryGetValue("gpiPort", out var portStr) &&
                        int.TryParse(portStr, out int port))
                    {
                        configuredPort = port;
                    }
                    
                    if (writerSettings.Parameters.TryGetValue("gpiTriggerState", out var stateStr) &&
                        bool.TryParse(stateStr, out bool state))
                    {
                        configuredState = state;
                    }
                }
            }
            
            // Only process if it's the configured port and state for this reader
            if (e.PortNumber == configuredPort && e.State == configuredState)
            {
                long eventId = Interlocked.Increment(ref lastGpiEventId);
                Console.WriteLine($"GPI event detected on {readerRole} reader, port {e.PortNumber}, State: {e.State}, Event ID: {eventId}");
                
                // Create timer for this GPI event
                var timer = new Stopwatch();
                timer.Start();
                gpiEventTimers[eventId] = timer;
                gpiEventVerified[eventId] = false;
                
                // Store which reader triggered the event (for GPO response)
                gpiEventReaders[eventId] = reader;
                gpiEventReaderRoles[eventId] = readerRole;
                
                // Log the GPI event with reader role info
                LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},N/A,N/A,N/A,N/A,0,0,GPI_Triggered_{readerRole},None,0,0,0,{e.PortNumber},True,0");
                
                // Get reader-specific GPO settings
                bool readerGpoEnabled = enableGpoOutput;
                ushort readerGpoPort = gpoPort;
                
                if (readerRole == "verifier" && settings.TryGetValue("verifier", out var vSettings) && 
                    vSettings.Parameters != null)
                {
                    if (vSettings.Parameters.TryGetValue("enableGpoOutput", out var enableStr))
                        readerGpoEnabled = bool.TryParse(enableStr, out bool enable) ? enable : readerGpoEnabled;
                        
                    if (vSettings.Parameters.TryGetValue("gpoPort", out var portStr))
                        readerGpoPort = ushort.TryParse(portStr, out ushort port) ? port : readerGpoPort;
                }
                else if (readerRole == "detector" && settings.TryGetValue("detector", out var dSettings) && 
                    dSettings.Parameters != null)
                {
                    if (dSettings.Parameters.TryGetValue("enableGpoOutput", out var enableStr))
                        readerGpoEnabled = bool.TryParse(enableStr, out bool enable) ? enable : readerGpoEnabled;
                        
                    if (dSettings.Parameters.TryGetValue("gpoPort", out var portStr))
                        readerGpoPort = ushort.TryParse(portStr, out ushort port) ? port : readerGpoPort;
                }
                else if (readerRole == "writer" && settings.TryGetValue("writer", out var wSettings) && 
                    wSettings.Parameters != null)
                {
                    if (wSettings.Parameters.TryGetValue("enableGpoOutput", out var enableStr))
                        readerGpoEnabled = bool.TryParse(enableStr, out bool enable) ? enable : readerGpoEnabled;
                        
                    if (wSettings.Parameters.TryGetValue("gpoPort", out var portStr))
                        readerGpoPort = ushort.TryParse(portStr, out ushort port) ? port : readerGpoPort;
                }
                
                // Store GPO information with the event
                gpiEventGpoEnabled[eventId] = readerGpoEnabled;
                gpiEventGpoPorts[eventId] = readerGpoPort;
                
                // Optionally trigger GPO immediately (will be reset if no tag found)
                if (readerGpoEnabled)
                {
                    try
                    {
                        // Set the output
                        reader.SetGpo(readerGpoPort, true);
                        Console.WriteLine($"GPO {readerGpoPort} activated on {readerRole} reader due to GPI event");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error setting GPO {readerGpoPort} on {readerRole} reader: {ex.Message}");
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
                
                // Get reader and role information for this event
                gpiEventReaders.TryGetValue(eventId, out ImpinjReader reader);
                gpiEventReaderRoles.TryGetValue(eventId, out string readerRole);
                
                // Get reader-specific timeout
                int timeoutMs = gpoVerificationTimeoutMs;
                
                // Get the timeout value from the specific reader settings if available
                if (!string.IsNullOrEmpty(readerRole) && settings.TryGetValue(readerRole, out var readerSettings) &&
                    readerSettings.Parameters != null && 
                    readerSettings.Parameters.TryGetValue("gpoVerificationTimeoutMs", out var timeoutStr))
                {
                    timeoutMs = ushort.TryParse(timeoutStr, out ushort timeout) ? timeout : timeoutMs;
                }
                
                // Check if timeout exceeded
                if (timer.ElapsedMilliseconds > timeoutMs)
                {
                    Console.WriteLine($"!!! GPI event {eventId} on {readerRole} reader timed out after {timer.ElapsedMilliseconds}ms without tag detection !!!");
                    
                    // Log the error with reader role
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},N/A,N/A,N/A,N/A,0,0,Missing_Tag_{readerRole},None,0,0,0,0,True,{timer.ElapsedMilliseconds}");
                    
                    // Trigger error GPO output if enabled and we have a valid reader
                    if (gpiEventGpoEnabled.TryGetValue(eventId, out bool gpoEnabled) && 
                        gpoEnabled && 
                        reader != null &&
                        gpiEventGpoPorts.TryGetValue(eventId, out ushort gpoPort))
                    {
                        try
                        {
                            // Set the output to indicate error (toggle)
                            reader.SetGpo(gpoPort, false);
                            Thread.Sleep(200);
                            reader.SetGpo(gpoPort, true);
                            Thread.Sleep(200);
                            reader.SetGpo(gpoPort, false);
                            Console.WriteLine($"GPO {gpoPort} error signal sent (toggled) on {readerRole} reader");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error setting GPO {gpoPort} on {readerRole} reader: {ex.Message}");
                        }
                    }
                    
                    // Remove from tracking
                    gpiEventTimers.TryRemove(eventId, out _);
                    gpiEventVerified.TryRemove(eventId, out _);
                    gpiEventReaders.TryRemove(eventId, out _);
                    gpiEventReaderRoles.TryRemove(eventId, out _);
                    gpiEventGpoEnabled.TryRemove(eventId, out _);
                    gpiEventGpoPorts.TryRemove(eventId, out _);
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
                        
                        // Get which reader triggered the event
                        gpiEventReaders.TryGetValue(eventId, out ImpinjReader eventReader);
                        gpiEventReaderRoles.TryGetValue(eventId, out string readerRole);
                        gpiEventGpoEnabled.TryGetValue(eventId, out bool gpoEnabled);
                        gpiEventGpoPorts.TryGetValue(eventId, out ushort eventGpoPort);
                        
                        Console.WriteLine($"Tag detected for GPI event {eventId} after {timer.ElapsedMilliseconds}ms: TID={tidHex}, EPC={epcHex}");
                        
                        // Mark this event as verified
                        gpiEventVerified[eventId] = true;
                        
                        // Set GPO to success if enabled
                        if (gpoEnabled && eventReader != null)
                        {
                            try
                            {
                                if (success)
                                {
                                    // Success signal (steady on)
                                    eventReader.SetGpo(eventGpoPort, true);
                                    Console.WriteLine($"GPO {eventGpoPort} success signal sent (ON) on {readerRole} reader");
                                    
                                    // Schedule GPO reset after 1 second
                                    new Timer(state => {
                                        try {
                                            if (eventReader != null) {
                                                eventReader.SetGpo(eventGpoPort, false);
                                                Console.WriteLine($"GPO {eventGpoPort} reset after success signal on {readerRole} reader");
                                            }
                                        } 
                                        catch (Exception) { /* Ignore timer errors */ }
                                    }, null, 1000, Timeout.Infinite);
                                }
                                else
                                {
                                    // Wrong EPC signal (double pulse)
                                    eventReader.SetGpo(eventGpoPort, false);
                                    Thread.Sleep(100);
                                    eventReader.SetGpo(eventGpoPort, true);
                                    Thread.Sleep(100);
                                    eventReader.SetGpo(eventGpoPort, false);
                                    Console.WriteLine($"GPO {eventGpoPort} wrong EPC signal sent (double pulse) on {readerRole} reader");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error setting GPO {eventGpoPort} on {readerRole} reader: {ex.Message}");
                            }
                        }
                        
                        // Log with GPI information
                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},{epcHex},0,{timer.ElapsedMilliseconds},{writeStatus}_{readerRole},None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},True,{timer.ElapsedMilliseconds}");
                        
                        // Remove from tracking
                        gpiEventTimers.TryRemove(eventId, out _);
                        gpiEventReaders.TryRemove(eventId, out _);
                        gpiEventReaderRoles.TryRemove(eventId, out _);
                        gpiEventGpoEnabled.TryRemove(eventId, out _);
                        gpiEventGpoPorts.TryRemove(eventId, out _);
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
                    ImpinjReader eventReader = null;
                    string readerRole = "unknown";
                    bool gpoEnabled = false;
                    ushort eventGpoPort = 1;
                    
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
                            
                            // Get reader information for this event
                            gpiEventReaders.TryGetValue(matchingEventId, out eventReader);
                            gpiEventReaderRoles.TryGetValue(matchingEventId, out readerRole);
                            gpiEventGpoEnabled.TryGetValue(matchingEventId, out gpoEnabled);
                            gpiEventGpoPorts.TryGetValue(matchingEventId, out eventGpoPort);
                            
                            // Mark as GPI triggered and verified
                            isGpiTriggered = true;
                            gpiEventVerified[matchingEventId] = true;
                            
                            Console.WriteLine($"Tag read operation completed for GPI event {matchingEventId} on {readerRole} reader: TID={tidHex}, successful={success}");
                            
                            // Set GPO signal based on verification result if enabled
                            if (gpoEnabled && eventReader != null)
                            {
                                try
                                {
                                    if (success)
                                    {
                                        // Success signal (steady on for 1 second)
                                        eventReader.SetGpo(eventGpoPort, true);
                                        Console.WriteLine($"GPO {eventGpoPort} set to ON (success) on {readerRole} reader for GPI event {matchingEventId}");
                                        
                                        // Schedule GPO reset after 1 second
                                        new Timer(state => {
                                            try {
                                                if (eventReader != null) {
                                                    eventReader.SetGpo(eventGpoPort, false);
                                                    Console.WriteLine($"GPO {eventGpoPort} reset after success signal on {readerRole} reader");
                                                }
                                            } 
                                            catch (Exception) { /* Ignore timer errors */ }
                                        }, null, 1000, Timeout.Infinite);
                                    }
                                    else
                                    {
                                        // EPC mismatch signal (triple pulse)
                                        eventReader.SetGpo(eventGpoPort, true);
                                        Thread.Sleep(100);
                                        eventReader.SetGpo(eventGpoPort, false);
                                        Thread.Sleep(100);
                                        eventReader.SetGpo(eventGpoPort, true);
                                        Thread.Sleep(100);
                                        eventReader.SetGpo(eventGpoPort, false);
                                        Thread.Sleep(100);
                                        eventReader.SetGpo(eventGpoPort, true);
                                        Thread.Sleep(100);
                                        eventReader.SetGpo(eventGpoPort, false);
                                        Console.WriteLine($"GPO {eventGpoPort} triple pulse (EPC mismatch) on {readerRole} reader for GPI event {matchingEventId}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error setting GPO {eventGpoPort} on {readerRole} reader: {ex.Message}");
                                }
                            }
                            
                            // Remove from tracking
                            gpiEventTimers.TryRemove(matchingEventId, out _);
                            gpiEventReaders.TryRemove(matchingEventId, out _);
                            gpiEventReaderRoles.TryRemove(matchingEventId, out _);
                            gpiEventGpoEnabled.TryRemove(matchingEventId, out _);
                            gpiEventGpoPorts.TryRemove(matchingEventId, out _);
                        }
                    } 
                    else 
                    {
                        try {
                            verifierReader.SetGpo(gpoPort, false);
                            Console.WriteLine($"GPO {gpoPort} reset after success signal");
                        }
                        catch (Exception) { /* Ignore timer errors */ }
                    }
                    
                    // Log tag read/write result, including GPI and reader information if applicable
                    string readerDesc = isGpiTriggered ? $"{readerRole}_reader" : "";
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{status}_{readerDesc},{lockStatus},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},{readResult.Tag.PeakRssiInDbm},{readResult.Tag.AntennaPortNumber},{isGpiTriggered},{gpiVerificationTime}");
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
                var metrics = new Dictionary<string, object>
                {
                    { "CycleCount", cycleCount.Count > 0 ? cycleCount.Values.Average() : 0 },
                    { "MaxCycle", cycleCount.Count > 0 ? cycleCount.Values.Max() : 0 },
                    { "AvgWriteTimeMs", swWriteTimers.Count > 0 ? swWriteTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0 },
                    { "AvgVerifyTimeMs", swVerifyTimers.Count > 0 ? swVerifyTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0 },
                    { "AvgLockTimeMs", swLockTimers.Count > 0 ? swLockTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0 },
                    { "LockedTags", lockedTags.Count },
                    { "LockEnabled", enableLock },
                    { "PermalockEnabled", enablePermalock },
                    { "GpiEventsTotal", lastGpiEventId },
                    { "GpiEventsVerified", gpiEventVerified.Count(kv => kv.Value) },
                    { "GpiEventsMissingTag", lastGpiEventId - gpiEventVerified.Count(kv => kv.Value) },
                    { "GpiEventsPending", gpiEventTimers.Count },
                    { "ElapsedSeconds", runTimer.Elapsed.TotalSeconds }
                };
                
                // Add reader-specific GPI/GPO settings to metrics
                if (settings.TryGetValue("verifier", out var verifierSettings) && 
                    verifierSettings.Parameters != null)
                {
                    string vGpiEnabled = verifierSettings.Parameters.TryGetValue("enableGpiTrigger", out var vStr) ? vStr : "false";
                    string vGpiPort = verifierSettings.Parameters.TryGetValue("gpiPort", out var vPortStr) ? vPortStr : "1";
                    string vGpoEnabled = verifierSettings.Parameters.TryGetValue("enableGpoOutput", out var vGpoStr) ? vGpoStr : "false";
                    string vGpoPort = verifierSettings.Parameters.TryGetValue("gpoPort", out var vGpoPortStr) ? vGpoPortStr : "1";
                    
                    metrics["VerifierGpiEnabled"] = vGpiEnabled;
                    metrics["VerifierGpiPort"] = vGpiPort;
                    metrics["VerifierGpoEnabled"] = vGpoEnabled;
                    metrics["VerifierGpoPort"] = vGpoPort;
                }
                
                if (settings.TryGetValue("detector", out var detectorSettings) && 
                    detectorSettings.Parameters != null)
                {
                    string dGpiEnabled = detectorSettings.Parameters.TryGetValue("enableGpiTrigger", out var dStr) ? dStr : "false";
                    string dGpiPort = detectorSettings.Parameters.TryGetValue("gpiPort", out var dPortStr) ? dPortStr : "1";
                    string dGpoEnabled = detectorSettings.Parameters.TryGetValue("enableGpoOutput", out var dGpoStr) ? dGpoStr : "false";
                    string dGpoPort = detectorSettings.Parameters.TryGetValue("gpoPort", out var dGpoPortStr) ? dGpoPortStr : "1";
                    
                    metrics["DetectorGpiEnabled"] = dGpiEnabled;
                    metrics["DetectorGpiPort"] = dGpiPort;
                    metrics["DetectorGpoEnabled"] = dGpoEnabled;
                    metrics["DetectorGpoPort"] = dGpoPort;
                }
                
                if (settings.TryGetValue("writer", out var writerSettings) && 
                    writerSettings.Parameters != null)
                {
                    string wGpiEnabled = writerSettings.Parameters.TryGetValue("enableGpiTrigger", out var wStr) ? wStr : "false";
                    string wGpiPort = writerSettings.Parameters.TryGetValue("gpiPort", out var wPortStr) ? wPortStr : "1";
                    string wGpoEnabled = writerSettings.Parameters.TryGetValue("enableGpoOutput", out var wGpoStr) ? wGpoStr : "false";
                    string wGpoPort = writerSettings.Parameters.TryGetValue("gpoPort", out var wGpoPortStr) ? wGpoPortStr : "1";
                    
                    metrics["WriterGpiEnabled"] = wGpiEnabled;
                    metrics["WriterGpiPort"] = wGpiPort;
                    metrics["WriterGpoEnabled"] = wGpoEnabled;
                    metrics["WriterGpoPort"] = wGpoPort;
                }

                return new JobExecutionStatus
                {
                    TotalTagsProcessed = status.TotalTagsProcessed,
                    SuccessCount = status.SuccessCount,
                    FailureCount = status.FailureCount,
                    ProgressPercentage = status.ProgressPercentage,
                    CurrentOperation = status.CurrentOperation,
                    RunTime = status.RunTime,
                    Metrics = metrics
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
            try
            {
                // Check each reader for GPO settings
                if (settings.TryGetValue("verifier", out var verifierSettings) &&
                    verifierSettings.Parameters != null &&
                    verifierSettings.Parameters.TryGetValue("enableGpoOutput", out var vGpoStr) &&
                    bool.TryParse(vGpoStr, out bool vGpoEnabled) &&
                    vGpoEnabled &&
                    verifierReader != null)
                {
                    // Get GPO port
                    ushort vGpoPort = gpoPort;
                    if (verifierSettings.Parameters.TryGetValue("gpoPort", out var vPortStr) &&
                        ushort.TryParse(vPortStr, out ushort vPort))
                    {
                        vGpoPort = vPort;
                    }
                    
                    // Turn off GPO
                    try
                    {
                        verifierReader.SetGpo(vGpoPort, false);
                        Console.WriteLine($"Turned off GPO {vGpoPort} on verifier reader during cleanup");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error turning off GPO on verifier reader: {ex.Message}");
                    }
                }
                
                // Check detector reader
                if (settings.TryGetValue("detector", out var detectorSettings) &&
                    detectorSettings.Parameters != null &&
                    detectorSettings.Parameters.TryGetValue("enableGpoOutput", out var dGpoStr) &&
                    bool.TryParse(dGpoStr, out bool dGpoEnabled) &&
                    dGpoEnabled &&
                    detectorReader != null)
                {
                    // Get GPO port
                    ushort dGpoPort = gpoPort;
                    if (detectorSettings.Parameters.TryGetValue("gpoPort", out var dPortStr) &&
                        ushort.TryParse(dPortStr, out ushort dPort))
                    {
                        dGpoPort = dPort;
                    }
                    
                    // Turn off GPO
                    try
                    {
                        detectorReader.SetGpo(dGpoPort, false);
                        Console.WriteLine($"Turned off GPO {dGpoPort} on detector reader during cleanup");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error turning off GPO on detector reader: {ex.Message}");
                    }
                }
                
                // Check writer reader
                if (settings.TryGetValue("writer", out var writerSettings) &&
                    writerSettings.Parameters != null &&
                    writerSettings.Parameters.TryGetValue("enableGpoOutput", out var wGpoStr) &&
                    bool.TryParse(wGpoStr, out bool wGpoEnabled) &&
                    wGpoEnabled &&
                    writerReader != null)
                {
                    // Get GPO port
                    ushort wGpoPort = gpoPort;
                    if (writerSettings.Parameters.TryGetValue("gpoPort", out var wPortStr) &&
                        ushort.TryParse(wPortStr, out ushort wPort))
                    {
                        wGpoPort = wPort;
                    }
                    
                    // Turn off GPO
                    try
                    {
                        writerReader.SetGpo(wGpoPort, false);
                        Console.WriteLine($"Turned off GPO {wGpoPort} on writer reader during cleanup");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error turning off GPO on writer reader: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during GPO cleanup: {ex.Message}");
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