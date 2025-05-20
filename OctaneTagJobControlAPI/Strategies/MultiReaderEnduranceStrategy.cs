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
using Impinj.TagUtils;

namespace OctaneTagJobControlAPI.Strategies
{
    /// <summary>
    /// Dual-reader endurance strategy with support for distributed reader roles.
    /// This strategy can operate with any combination of detector, writer, and verifier readers,
    /// allowing for deployment across multiple machines or instances.
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
        private readonly ConcurrentDictionary<string, ReaderMetrics> readerMetrics = new();
        private readonly ConcurrentDictionary<string, Stopwatch> swWriteTimers = new();
        private readonly ConcurrentDictionary<string, Stopwatch> swVerifyTimers = new();
        private readonly ConcurrentDictionary<string, Stopwatch> swLockTimers = new();
        private Timer successCountTimer;
        private JobExecutionStatus status = new();
        private readonly Stopwatch runTimer = new();

        // Encoding configuration
        private readonly string _sku;
        private readonly string _epcHeader;
        private readonly EpcEncodingMethod _encodingMethod;
        private readonly int _companyPrefixLength;
        private readonly int _itemReference;
        private string _baseEpcHex = null;

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
        private ConcurrentDictionary<long, ImpinjReader> gpiEventReaders = new();
        private ConcurrentDictionary<long, string> gpiEventReaderRoles = new();
        private ConcurrentDictionary<long, bool> gpiEventGpoEnabled = new();
        private ConcurrentDictionary<long, ushort> gpiEventGpoPorts = new();
        private long lastGpiEventId = 0;

        // Role availability flags
        private bool hasDetectorRole = false;
        private bool hasWriterRole = false;
        private bool hasVerifierRole = false;

        // Operational mode flags based on available readers
        private bool operatingAsDetectorOnly = false;
        private bool operatingAsWriterOnly = false;
        private bool operatingAsVerifierOnly = false;
        private bool operatingAsWriterVerifier = false;
        private bool operatingAsDetectorWriter = false;

        /// <summary>
        /// Initializes a new instance of the MultiReaderEnduranceStrategy class
        /// with support for distributed reader roles.
        /// </summary>
        /// <param name="detectorHostname">The hostname of the detector reader (can be null/empty if not used)</param>
        /// <param name="writerHostname">The hostname of the writer reader (can be null/empty if not used)</param>
        /// <param name="verifierHostname">The hostname of the verifier reader (can be null/empty if not used)</param>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="readerSettings">Dictionary of reader settings</param>
        /// <param name="serviceProvider">Optional service provider</param>
        public MultiReaderEnduranceStrategy(
            string detectorHostname,
            string writerHostname,
            string verifierHostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings,
            string epcHeader = "E7",
            string sku = null,
            string encodingMethod = "BasicWithTidSuffix",
            int companyPrefixLength = 6,
            int itemReference = 0,
            IServiceProvider serviceProvider = null)
            : base(detectorHostname, writerHostname, verifierHostname, logFile, readerSettings, serviceProvider)
        {
            _epcHeader = epcHeader;
            _sku = sku;
            _encodingMethod = Enum.TryParse(encodingMethod, true, out EpcEncodingMethod method) ? method : EpcEncodingMethod.BasicWithTidSuffix;
            _itemReference = itemReference;
            _companyPrefixLength = companyPrefixLength; 
            status.CurrentOperation = "Initialized";
            TagOpController.Instance.CleanUp();


            // Determine which reader roles are available
            hasDetectorRole = !string.IsNullOrWhiteSpace(detectorHostname) &&
                           readerSettings.ContainsKey("detector") &&
                           !string.IsNullOrWhiteSpace(readerSettings["detector"].Hostname);

            hasWriterRole = !string.IsNullOrWhiteSpace(writerHostname) &&
                         readerSettings.ContainsKey("writer") &&
                         !string.IsNullOrWhiteSpace(readerSettings["writer"].Hostname);

            hasVerifierRole = !string.IsNullOrWhiteSpace(verifierHostname) &&
                           readerSettings.ContainsKey("verifier") &&
                           !string.IsNullOrWhiteSpace(readerSettings["verifier"].Hostname);

            // Initialize reader metrics after roles are determined
            if (hasDetectorRole)
            {
                readerMetrics["detector"] = new ReaderMetrics
                {
                    Role = "detector",
                    Hostname = detectorHostname,
                    ReaderID = settings.TryGetValue("detector", out var detSettings) && 
                              detSettings.Parameters != null && 
                              detSettings.Parameters.TryGetValue("ReaderID", out var detId) ? detId : "Detector-01"
                };
            }

            if (hasWriterRole)
            {
                readerMetrics["writer"] = new ReaderMetrics
                {
                    Role = "writer",
                    Hostname = writerHostname,
                    ReaderID = settings.TryGetValue("writer", out var writerSettings) && 
                              writerSettings.Parameters != null && 
                              writerSettings.Parameters.TryGetValue("ReaderID", out var writerId) ? writerId : "Writer-01",
                    LockEnabled = enableLock,
                    PermalockEnabled = enablePermalock
                };
            }

            if (hasVerifierRole)
            {
                readerMetrics["verifier"] = new ReaderMetrics
                {
                    Role = "verifier",
                    Hostname = verifierHostname,
                    ReaderID = settings.TryGetValue("verifier", out var verifierSettings) && 
                              verifierSettings.Parameters != null && 
                              verifierSettings.Parameters.TryGetValue("ReaderID", out var verifierId) ? verifierId : "Verifier-01"
                };
            }

            // Determine operational mode based on available readers
            operatingAsDetectorOnly = hasDetectorRole && !hasWriterRole && !hasVerifierRole;
            operatingAsWriterOnly = !hasDetectorRole && hasWriterRole && !hasVerifierRole;
            operatingAsVerifierOnly = !hasDetectorRole && !hasWriterRole && hasVerifierRole;
            operatingAsWriterVerifier = !hasDetectorRole && hasWriterRole && hasVerifierRole;
            operatingAsDetectorWriter = hasDetectorRole && hasWriterRole && !hasVerifierRole;

            // Validate that at least one reader is available
            if (!hasDetectorRole && !hasWriterRole && !hasVerifierRole)
            {
                throw new ArgumentException("At least one reader role (detector, writer, or verifier) must be configured");
            }
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                status.CurrentOperation = "Starting";
                runTimer.Start();

                // Log which reader roles are active
                string activeRoles = "";
                if (hasDetectorRole) activeRoles += "Detector ";
                if (hasWriterRole) activeRoles += "Writer ";
                if (hasVerifierRole) activeRoles += "Verifier ";

                Console.WriteLine($"=== Multiple Reader Endurance Test (Active Roles: {activeRoles.Trim()}) ===");
                Console.WriteLine("Press 'q' to stop the test and return to menu.");

                // Extract configuration settings from parameters
                ExtractConfigurationSettings();

                // Configure only the available readers
                try
                {
                    if (hasDetectorRole)
                    {
                        ConfigureDetectorReader();
                        Console.WriteLine($"Configured detector reader at {detectorHostname}");
                    }

                    if (hasWriterRole)
                    {
                        ConfigureWriterReader();
                        Console.WriteLine($"Configured writer reader at {writerHostname}");
                    }

                    if (hasVerifierRole)
                    {
                        ConfigureVerifierReader();
                        Console.WriteLine($"Configured verifier reader at {verifierHostname}");
                    }
                }
                catch (Exception ex)
                {
                    status.CurrentOperation = "Configuration Error";
                    Console.WriteLine($"Error configuring readers: {ex.Message}");
                    throw;
                }

                // Register event handlers only for available readers
                if (hasDetectorRole)
                {
                    detectorReader.TagsReported += OnTagsReportedDetector;

                    if (enableGpiTrigger &&
                        settings.TryGetValue("detector", out var detectorSettings) &&
                        detectorSettings.Parameters != null &&
                        detectorSettings.Parameters.TryGetValue("enableGpiTrigger", out var dGpiStr) &&
                        bool.TryParse(dGpiStr, out bool dGpiEnabled) &&
                        dGpiEnabled)
                    {
                        detectorReader.GpiChanged += OnGpiChanged;
                        Console.WriteLine($"GPI trigger enabled on detector reader");
                    }
                }

                if (hasWriterRole)
                {
                    writerReader.TagsReported += OnTagsReportedWriter;
                    writerReader.TagOpComplete += OnTagOpComplete;

                    if (enableGpiTrigger &&
                        settings.TryGetValue("writer", out var writerSettings) &&
                        writerSettings.Parameters != null &&
                        writerSettings.Parameters.TryGetValue("enableGpiTrigger", out var wGpiStr) &&
                        bool.TryParse(wGpiStr, out bool wGpiEnabled) &&
                        wGpiEnabled)
                    {
                        writerReader.GpiChanged += OnGpiChanged;
                        Console.WriteLine($"GPI trigger enabled on writer reader");
                    }
                }

                if (hasVerifierRole)
                {
                    verifierReader.TagsReported += OnTagsReportedVerifier;
                    verifierReader.TagOpComplete += OnTagOpComplete;

                    if (enableGpiTrigger &&
                        settings.TryGetValue("verifier", out var verifierSettings) &&
                        verifierSettings.Parameters != null &&
                        verifierSettings.Parameters.TryGetValue("enableGpiTrigger", out var vGpiStr) &&
                        bool.TryParse(vGpiStr, out bool vGpiEnabled) &&
                        vGpiEnabled)
                    {
                        verifierReader.GpiChanged += OnGpiChanged;
                        Console.WriteLine($"GPI trigger enabled on verifier reader");
                    }
                }

                // Start the available readers
                if (hasDetectorRole)
                {
                    detectorReader.Start();
                    Console.WriteLine("Started detector reader");
                }

                if (hasWriterRole)
                {
                    writerReader.Start();
                    Console.WriteLine("Started writer reader");
                }

                if (hasVerifierRole)
                {
                    verifierReader.Start();
                    Console.WriteLine("Started verifier reader");
                }

                // Update status
                status.CurrentOperation = "Running";

                // Create CSV header if needed
                if (!File.Exists(logFile))
                {
                    // Update header to include GPI/GPO status and reader role info
                    LogToCsv("Timestamp,TID,Previous_EPC,Expected_EPC,Verified_EPC,WriteTime_ms,VerifyTime_ms,Result,LockStatus,LockTime_ms,CycleCount,RSSI,AntennaPort,GpiTriggered,VerificationTimeMs,ReaderRole");
                }

                // Initialize success count timer
                successCountTimer = new Timer(LogSuccessCount, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                    status.RunTime = runTimer.Elapsed;

                    // Check for GPI events that timed out without tag detection
                    if (enableGpiTrigger)
                    {
                        CheckGpiTimeouts();
                    }
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
                if (hasWriterRole && settings.TryGetValue("writer", out var writerSettings) && writerSettings.Parameters != null)
                {
                    // Extract lock settings
                    if (writerSettings.Parameters.TryGetValue("enableLock", out var lockStr))
                        enableLock = bool.TryParse(lockStr, out bool lockVal) ? lockVal : false;

                    if (writerSettings.Parameters.TryGetValue("enablePermalock", out var permalockStr))
                        enablePermalock = bool.TryParse(permalockStr, out bool permalock) ? permalock : false;
                }

                // Extract GPI settings from verifier reader (primary location for GPI/GPO settings)
                if (hasVerifierRole && settings.TryGetValue("verifier", out var verifierSettings) && verifierSettings.Parameters != null)
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

                // Also check detector reader parameters if available
                if (hasDetectorRole && settings.TryGetValue("detector", out var detectorSettings) && detectorSettings.Parameters != null)
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
                if (_serviceProvider != null)
                {
                    // Try to get EnduranceTestConfiguration from service provider if available
                    var configService = _serviceProvider.GetService(typeof(EnduranceTestConfiguration)) as EnduranceTestConfiguration;
                    if (configService != null)
                    {
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
                    }
                }

                string activeRoles = "";
                if (hasDetectorRole) activeRoles += "Detector ";
                if (hasWriterRole) activeRoles += "Writer ";
                if (hasVerifierRole) activeRoles += "Verifier ";

                Console.WriteLine($"Configuration (Active Roles: {activeRoles.Trim()}): Lock={enableLock}, Permalock={enablePermalock}, " +
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
            if (!hasVerifierRole)
            {
                return null; // Skip if no verifier role
            }

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
                    settings.Gpis.GetGpi(verifierGpiPort).DebounceInMs = 500;

                    // Configure GPO ports
                    int numOfGPOs = settings.Gpos.Length;

                    settings.Gpos.GetGpo(1).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(1).GpoPulseDurationMsec = 500;

                    settings.Gpos.GetGpo(2).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(2).GpoPulseDurationMsec = 500;

                    settings.Gpos.GetGpo(3).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(3).GpoPulseDurationMsec = 500;

                    // Only set GPO4 if the reader has 4 GPOs
                    if (numOfGPOs == 4)
                    {
                        settings.Gpos.GetGpo(4).Mode = GpoMode.Pulsed;
                        settings.Gpos.GetGpo(4).GpoPulseDurationMsec = 500;
                    }

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
            if (!hasDetectorRole)
            {
                return null; // Skip if no detector role
            }

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
                    settings.Gpis.GetGpi(detectorGpiPort).DebounceInMs = 500;

                    // Configure GPO ports
                    int numOfGPOs = settings.Gpos.Length;

                    settings.Gpos.GetGpo(1).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(1).GpoPulseDurationMsec = 500;

                    settings.Gpos.GetGpo(2).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(2).GpoPulseDurationMsec = 500;

                    settings.Gpos.GetGpo(3).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(3).GpoPulseDurationMsec = 500;

                    // Only set GPO4 if the reader has 4 GPOs
                    if (numOfGPOs == 4)
                    {
                        settings.Gpos.GetGpo(4).Mode = GpoMode.Pulsed;
                        settings.Gpos.GetGpo(4).GpoPulseDurationMsec = 500;
                    }

                    // Apply the settings to the reader
                    detectorReader.ApplySettings(settings);

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
            if (!hasWriterRole)
            {
                return null; // Skip if no writer role
            }

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
                    settings.Gpis.GetGpi(writerGpiPort).DebounceInMs = 500;

                    // Configure GPO ports
                    int numOfGPOs = settings.Gpos.Length;

                    settings.Gpos.GetGpo(1).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(1).GpoPulseDurationMsec = 500;

                    settings.Gpos.GetGpo(2).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(2).GpoPulseDurationMsec = 500;

                    settings.Gpos.GetGpo(3).Mode = GpoMode.Pulsed;
                    settings.Gpos.GetGpo(3).GpoPulseDurationMsec = 500;

                    // Apply the settings to the reader
                    writerReader.ApplySettings(settings);

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
                LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},N/A,N/A,N/A,N/A,0,0,GPI_Triggered_{readerRole},None,0,0,0,{e.PortNumber},True,0,{readerRole}");

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
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},N/A,N/A,N/A,N/A,0,0,Missing_Tag_{readerRole},None,0,0,0,0,True,{timer.ElapsedMilliseconds},{readerRole}");

                    // Trigger error GPO output if enabled and we have a valid reader
                    if (gpiEventGpoEnabled.TryGetValue(eventId, out bool gpoEnabled) &&
                        gpoEnabled &&
                        reader != null &&
                        gpiEventGpoPorts.TryGetValue(eventId, out ushort gpoPort))
                    {
                        try
                        {
                            // Set the output to indicate error (toggle)
                            reader.SetGpo(gpoPort, true);
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
            if (!hasDetectorRole || report == null || IsCancellationRequested())
                return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                var epcHex = tag.Epc?.ToHexString() ?? string.Empty;
                if (string.IsNullOrEmpty(tidHex) || TagOpController.Instance.IsTidProcessed(tidHex))
                    continue;

                Console.WriteLine($"Detector: New tag detected. TID: {tidHex}, Current EPC: {epcHex}");

                // Generate a new EPC if one is not already recorded using the detector logic
                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                if (string.IsNullOrEmpty(expectedEpc))
                {            
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex, _sku, _companyPrefixLength, _encodingMethod);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"Detector: Assigned new EPC for TID {tidHex}: {expectedEpc}");

                    // Log the tag detection
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},N/A,0,0,DetectedNewTag,None,0,0,{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,detector");

                    // If we also have the writer role, trigger the write operation locally
                    if (hasWriterRole)
                    {
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
                    // Otherwise, just record the detection for distributed processing
                    else
                    {
                        TagOpController.Instance.RecordTagSeen(tidHex, epcHex, expectedEpc, TagOpController.Instance.GetChipModelName(tag));
                    }
                }
            }
        }

        /// <summary>
        /// Event handler for tag reports from the writer reader
        /// </summary>
        private void OnTagsReportedWriter(ImpinjReader sender, TagReport report)
        {
            if (!hasWriterRole || report == null || IsCancellationRequested())
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

                // If no expected EPC exists, generate one using the writer logic
                if (string.IsNullOrEmpty(expectedEpc))
                {
                    Console.WriteLine($"Writer: New target TID found: {tidHex} Chip {TagOpController.Instance.GetChipModel(tag)}");
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex, _sku, _companyPrefixLength, _encodingMethod);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"Writer: New tag found. TID: {tidHex}. Assigning new EPC: {epcHex} -> {expectedEpc}");

                    // Log the tag detection by the writer role
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},N/A,0,0,DetectedNewTagByWriter,None,0,0,{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,writer");
                }

                if (!expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase))
                {
                    // Trigger the write operation using the writer reader
                    TagOpController.Instance.TriggerWriteAndVerify(
                        tag,
                        expectedEpc,
                        sender,
                        cancellationToken,
                        swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                        newAccessPassword,
                        true,
                        1,
                        hasVerifierRole ? false : true, // Only self-verify if no verifier role available
                        3);
                }
                else
                {
                    if (expectedEpc != null && expectedEpc.Equals(epcHex, StringComparison.OrdinalIgnoreCase))
                    {
                        TagOpController.Instance.HandleVerifiedTag(tag, tidHex, expectedEpc, swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()), cycleCount, tag, TagOpController.Instance.GetChipModelName(tag), logFile);
                        continue;
                    }
                }
            }
        }

        /// <summary>
        /// Event handler for tag reports from the verifier reader
        /// </summary>
        private void OnTagsReportedVerifier(ImpinjReader sender, TagReport report)
        {
            if (!hasVerifierRole || report == null || IsCancellationRequested())
                return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                if (string.IsNullOrEmpty(tidHex))
                    continue;

                var epcHex = tag.Epc?.ToHexString() ?? string.Empty;
                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                // If no expected EPC exists (this can happen in distributed mode)
                if (string.IsNullOrEmpty(expectedEpc))
                {
                    // When operating as verifier only, we might see tags that detector/writer processed
                    // We'll use a fallback approach to handle this case
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex, _sku, _companyPrefixLength, _encodingMethod);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"Verifier (fallback detection): TID {tidHex}. Current EPC: {epcHex}, Expected: {expectedEpc}");

                    // Log this situation - seen by verifier first
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},N/A,0,0,DetectedByVerifierFirst,None,0,0,{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,verifier");
                }

                bool success = expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase);
                var writeStatus = success ? "Success" : "Failure";
                Console.WriteLine($"Verifier - TID {tidHex} - current EPC: {epcHex} Expected EPC: {expectedEpc} Operation Status [{writeStatus}]");

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
                                }
                                else
                                {
                                    // Wrong EPC signal (double pulse)
                                    eventReader.SetGpo(eventGpoPort, true);
                                    Thread.Sleep(500);
                                    eventReader.SetGpo(eventGpoPort, true);
                                    Console.WriteLine($"GPO {eventGpoPort} wrong EPC signal sent (double pulse) on {readerRole} reader");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error setting GPO {eventGpoPort} on {readerRole} reader: {ex.Message}");
                            }
                        }

                        // Log with GPI information
                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},{epcHex},0,{timer.ElapsedMilliseconds},{writeStatus}_{readerRole},None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},True,{timer.ElapsedMilliseconds},verifier");

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
                        Console.WriteLine($"Verifier - TID {tidHex} verified successfully. Current EPC: {epcHex} - Written tags registered {TagOpController.Instance.GetSuccessCount()} (TIDs processed)");
                    }
                }
                else if (!string.IsNullOrEmpty(expectedEpc))
                {
                    if (!expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase))
                    {
                        Console.WriteLine($"Verification mismatch for TID {tidHex}: expected {expectedEpc}, read {epcHex}. Verification failed.");

                        // If we also have writer role, retry the write directly
                        if (hasWriterRole)
                        {
                            Console.WriteLine($"Verification failed - retrying write locally (since we have writer role)");
                            // Retry writing using the expected EPC via the writer reader
                            TagOpController.Instance.TriggerWriteAndVerify(
                                tag,
                                expectedEpc,
                                writerReader,
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
                            // Just log the failure
                            LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},VerificationFailed,0,0,VerifyMismatch,None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,verifier");
                            TagOpController.Instance.RecordResult(tidHex, "VerificationFailed", false);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Verifier - TID {tidHex} verified successfully. Current EPC: {epcHex} - Written tags registered {TagOpController.Instance.GetSuccessCount()} (TIDs processed)");
                    }
                }
            }
        }

        /// <summary>
        /// Common event handler for tag operation completions from both readers
        /// </summary>
        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            // Determine the reader role
            string readerRole = "unknown";
            if (sender == verifierReader)
                readerRole = "verifier";
            else if (sender == writerReader)
                readerRole = "writer";
            else if (sender == detectorReader)
                readerRole = "detector";

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
                        Console.WriteLine($"Write operation succeeded for TID {tidHex} on {readerRole} reader.");

                        // If we have a verifier role, trigger verification there
                        if (hasVerifierRole)
                        {
                            TagOpController.Instance.TriggerVerificationRead(
                                result.Tag,
                                verifierReader,
                                cancellationToken,
                                swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                                newAccessPassword);
                        }
                        // Otherwise if we're a writer-only role with no verifier, do self-verification
                        else if (operatingAsWriterOnly)
                        {
                            TagOpController.Instance.TriggerVerificationRead(
                                result.Tag,
                                writerReader,
                                cancellationToken,
                                swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                                newAccessPassword);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Write operation failed for TID {tidHex} on {readerRole} reader.");
                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},WriteFailed,{swWriteTimers[tidHex].ElapsedMilliseconds},0,Failure,None,0,{cycleCount.GetOrAdd(tidHex, 0)},{result.Tag.PeakRssiInDbm},{result.Tag.AntennaPortNumber},False,0,{readerRole}");
                    }
                }
                else if (result is TagReadOpResult readResult)
                {
                    swVerifyTimers[tidHex].Stop();

                    var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    if (string.IsNullOrEmpty(expectedEpc))
                    {
                        expectedEpc = TagOpController.Instance.GetNextEpcForTag(readResult.Tag.Epc.ToHexString(), tidHex, _sku, _companyPrefixLength, _encodingMethod);
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
                    string gpiReaderRole = "unknown";
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
                            gpiEventReaderRoles.TryGetValue(matchingEventId, out gpiReaderRole);
                            gpiEventGpoEnabled.TryGetValue(matchingEventId, out gpoEnabled);
                            gpiEventGpoPorts.TryGetValue(matchingEventId, out eventGpoPort);

                            // Mark as GPI triggered and verified
                            isGpiTriggered = true;
                            gpiEventVerified[matchingEventId] = true;

                            Console.WriteLine($"Tag read operation completed for GPI event {matchingEventId} on {gpiReaderRole} reader: TID={tidHex}, successful={success}");

                            // Set GPO signal based on verification result if enabled
                            if (gpoEnabled && eventReader != null)
                            {
                                try
                                {
                                    if (success)
                                    {
                                        // Success signal (steady on for 1 second)
                                        eventReader.SetGpo(eventGpoPort, true);
                                        Console.WriteLine($"GPO {eventGpoPort} set to ON (success) on {gpiReaderRole} reader for GPI event {matchingEventId}");

                                        // Schedule GPO reset after 1 second
                                        new Timer(state => {
                                            try
                                            {
                                                if (eventReader != null)
                                                {
                                                    eventReader.SetGpo(eventGpoPort, false);
                                                    Console.WriteLine($"GPO {eventGpoPort} reset after success signal on {gpiReaderRole} reader");
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
                                        Console.WriteLine($"GPO {eventGpoPort} triple pulse (EPC mismatch) on {gpiReaderRole} reader for GPI event {matchingEventId}");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error setting GPO {eventGpoPort} on {gpiReaderRole} reader: {ex.Message}");
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

                    // Log tag read/write result, including GPI and reader information if applicable
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{status},{lockStatus},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},{readResult.Tag.PeakRssiInDbm},{readResult.Tag.AntennaPortNumber},{isGpiTriggered},{gpiVerificationTime},{readerRole}");
                    TagOpController.Instance.RecordResult(tidHex, status, success);

                    Console.WriteLine($"Verification result for TID {tidHex} on {readerRole} reader: {status}");

                    cycleCount.AddOrUpdate(tidHex, 1, (key, oldValue) => oldValue + 1);

                    if (!success)
                    {
                        // If we have writer role in the same instance, try to rewrite
                        if (hasWriterRole)
                        {
                            try
                            {
                                Console.WriteLine($"Verification failed - retrying write locally (since we have writer role)");
                                TagOpController.Instance.TriggerWriteAndVerify(
                                    readResult.Tag,
                                    expectedEpc,
                                    writerReader,
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
                        else
                        {
                            // Log the failure and wait for writer instance to handle it
                            Console.WriteLine($"Verification failed - waiting for writer instance to handle it");
                            LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},VerificationFailed,{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},NeedsRetry,None,0,{cycleCount.GetOrAdd(tidHex, 0)},{readResult.Tag.PeakRssiInDbm},{readResult.Tag.AntennaPortNumber},False,0,{readerRole}");
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

                    Console.WriteLine($"{lockStatus} operation for TID {tidHex} on {readerRole} reader: {lockOpStatus} in {lockTimer.ElapsedMilliseconds}ms");

                    // If lock failed but EPC is still correct, we've still completed the main operation
                    var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    var currentEpc = lockResult.Tag.Epc?.ToHexString() ?? "N/A";

                    bool epcCorrect = !string.IsNullOrEmpty(expectedEpc) &&
                                      expectedEpc.Equals(currentEpc, StringComparison.InvariantCultureIgnoreCase);

                    // Overall success is based on the EPC being correct, even if lock fails
                    if (epcCorrect)
                    {
                        TagOpController.Instance.RecordResult(tidHex, "Success", true);
                    }

                    // Log the lock operation
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{currentEpc},{expectedEpc},{currentEpc},{swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},{swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},Success,{(success ? lockStatus : lockStatus + "Failed")},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},{lockResult.Tag.PeakRssiInDbm},{lockResult.Tag.AntennaPortNumber},False,0,{readerRole}");
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

                string activeRoles = "";
                if (hasDetectorRole) activeRoles += "Detector ";
                if (hasWriterRole) activeRoles += "Writer ";
                if (hasVerifierRole) activeRoles += "Verifier ";

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
                    status.Metrics["ActiveRoles"] = activeRoles.Trim();
                }

                Console.WriteLine($"[{activeRoles.Trim()}] Total Read [{totalReadCount}] Success count: [{successCount}] Locked/Permalocked: [{lockedCount}] GPI Events: {gpiEventsTotal} (Verified: {gpiEventsVerified}, Missing: {gpiEventsTotal - gpiEventsVerified}, Pending: {gpiEventsPending})");
            }
            catch (Exception ex)
            {
                // Ignore timer exceptions
                Console.WriteLine($"Error in LogSuccessCount: {ex.Message}");
            }
        }

        /// <summary>
        /// Overrides the base class CleanupReaders method to handle the case where not all readers are available
        /// </summary>
        protected override void CleanupReaders()
        {
            try
            {
                if (hasDetectorRole && detectorReader != null)
                {
                    detectorReader.Stop();
                    detectorReader.Disconnect();
                    Console.WriteLine("Disconnected detector reader");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during detector reader cleanup: {ex.Message}");
            }

            try
            {
                if (hasWriterRole && writerReader != null)
                {
                    writerReader.Stop();
                    writerReader.Disconnect();
                    Console.WriteLine("Disconnected writer reader");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during writer reader cleanup: {ex.Message}");
            }

            try
            {
                if (hasVerifierRole && verifierReader != null)
                {
                    verifierReader.Stop();
                    verifierReader.Disconnect();
                    Console.WriteLine("Disconnected verifier reader");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during verifier reader cleanup: {ex.Message}");
            }
        }

        public override JobExecutionStatus GetStatus()
        {
            lock (status)
            {
                string activeRoles = "";
                if (hasDetectorRole) activeRoles += "Detector ";
                if (hasWriterRole) activeRoles += "Writer ";
                if (hasVerifierRole) activeRoles += "Verifier ";

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
                    { "ElapsedSeconds", runTimer.Elapsed.TotalSeconds },
                    { "ActiveRoles", activeRoles.Trim() },
                    { "HasDetectorRole", hasDetectorRole },
                    { "HasWriterRole", hasWriterRole },
                    { "HasVerifierRole", hasVerifierRole }
                };

                // Add reader-specific GPI/GPO settings to metrics
                if (hasVerifierRole && settings.TryGetValue("verifier", out var verifierSettings) &&
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

                if (hasDetectorRole && settings.TryGetValue("detector", out var detectorSettings) &&
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

                if (hasWriterRole && settings.TryGetValue("writer", out var writerSettings) &&
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

                // Update reader-specific metrics
                var readerMetricsDict = new Dictionary<string, ReaderMetrics>();
                foreach (var kvp in readerMetrics)
                {
                    var role = kvp.Key;
                    var readerMetric = kvp.Value;

                    // Update common metrics
                    readerMetric.ReadRate = TagOpController.Instance.GetReadRate();
                    readerMetric.SuccessCount = TagOpController.Instance.GetSuccessCount();
                    readerMetric.FailureCount = TagOpController.Instance.GetFailureCount();

                    // Update role-specific metrics
                    switch (role)
                    {
                        case "writer":
                            readerMetric.AvgWriteTimeMs = swWriteTimers.Count > 0 ? swWriteTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0;
                            readerMetric.LockedTags = lockedTags.Count;
                            break;
                        case "verifier":
                            readerMetric.AvgVerifyTimeMs = swVerifyTimers.Count > 0 ? swVerifyTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0;
                            break;
                    }

                    readerMetricsDict[role] = readerMetric;
                }

                // Add reader metrics to the overall metrics
                metrics["ReaderMetrics"] = readerMetricsDict;

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
                Description = "Performs endurance testing using multiple readers for detection, writing, and verification with support for distributed reader roles",
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
                if (hasVerifierRole &&
                    settings.TryGetValue("verifier", out var verifierSettings) &&
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
                if (hasDetectorRole &&
                    settings.TryGetValue("detector", out var detectorSettings) &&
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
                if (hasWriterRole &&
                    settings.TryGetValue("writer", out var writerSettings) &&
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