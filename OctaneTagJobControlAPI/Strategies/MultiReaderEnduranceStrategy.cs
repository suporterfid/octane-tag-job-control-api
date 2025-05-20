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
            : base(
                   detectorHostname,
            writerHostname,
            verifierHostname,
            logFile,
            readerSettings,
            epcHeader,
            sku,
            encodingMethod,
            companyPrefixLength,
            itemReference,
            serviceProvider
                  )
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

                // Registrar quais papéis de leitor estão ativos
                string activeRoles = "";
                if (hasDetectorRole) activeRoles += "Detector ";
                if (hasWriterRole) activeRoles += "Writer ";
                if (hasVerifierRole) activeRoles += "Verifier ";

                Console.WriteLine($"=== Multiple Reader Endurance Test (Active Roles: {activeRoles.Trim()}) ===");
                Console.WriteLine("Press 'q' to stop the test and return to menu.");

                // Extrair configurações de parâmetros
                ExtractConfigurationSettings();

                // Configurar apenas os leitores disponíveis
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

                // Registrar manipuladores de eventos apenas para leitores disponíveis
                RegisterEventHandlers();

                // Criar cabeçalho CSV se necessário
                if (!File.Exists(logFile))
                {
                    // Atualizar cabeçalho para incluir status GPI/GPO e informações de papel do leitor
                    LogToCsv("Timestamp,TID,Previous_EPC,Expected_EPC,Verified_EPC,WriteTime_ms,VerifyTime_ms,Result,LockStatus,LockTime_ms,CycleCount,RSSI,AntennaPort,GpiTriggered,VerificationTimeMs,ReaderRole");
                }

                // Iniciar os leitores disponíveis
                StartAvailableReaders();

                // Atualizar status
                status.CurrentOperation = "Running";

                // Inicializar timer de contagem de sucesso
                successCountTimer = new Timer(LogSuccessCount, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));

                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                    status.RunTime = runTimer.Elapsed;

                    // Verificar eventos GPI que expiraram sem detecção de tag
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

        // Método auxiliar para registrar manipuladores de eventos
        private void RegisterEventHandlers()
        {
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
        }

        // Método auxiliar para iniciar leitores disponíveis
        private void StartAvailableReaders()
        {
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
        // Método para verificar timeouts de eventos GPI
        private void CheckGpiTimeouts()
        {
            if (!enableGpiTrigger) return;

            foreach (var entry in gpiEventTimers.ToArray())
            {
                long eventId = entry.Key;
                Stopwatch timer = entry.Value;

                // Pular eventos que foram verificados
                if (gpiEventVerified.TryGetValue(eventId, out bool verified) && verified)
                    continue;

                // Obter informações do leitor e papel para este evento
                gpiEventReaders.TryGetValue(eventId, out ImpinjReader reader);
                gpiEventReaderRoles.TryGetValue(eventId, out string readerRole);

                // Obter o valor de timeout específico do leitor
                int timeoutMs = gpoVerificationTimeoutMs;

                // Obter o valor de timeout das configurações específicas do leitor, se disponível
                if (!string.IsNullOrEmpty(readerRole) && settings.TryGetValue(readerRole, out var readerSettings) &&
                    readerSettings.Parameters != null &&
                    readerSettings.Parameters.TryGetValue("gpoVerificationTimeoutMs", out var timeoutStr))
                {
                    timeoutMs = ushort.TryParse(timeoutStr, out ushort timeout) ? timeout : timeoutMs;
                }

                // Verificar se o timeout foi excedido
                if (timer.ElapsedMilliseconds > timeoutMs)
                {
                    Console.WriteLine($"!!! GPI event {eventId} on {readerRole} reader timed out after {timer.ElapsedMilliseconds}ms without tag detection !!!");

                    // Registar o erro com o papel do leitor
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},N/A,N/A,N/A,N/A,0,0,Missing_Tag_{readerRole},None,0,0,0,0,True,{timer.ElapsedMilliseconds},{readerRole}");

                    // Acionar saída de GPO de erro se habilitada e temos um leitor válido
                    if (gpiEventGpoEnabled.TryGetValue(eventId, out bool gpoEnabled) &&
                        gpoEnabled &&
                        reader != null &&
                        gpiEventGpoPorts.TryGetValue(eventId, out ushort gpoPort))
                    {
                        try
                        {
                            // Definir a saída para indicar erro (alternar)
                            reader.SetGpo(gpoPort, true);

                            // Programar reset de GPO após 500ms
                            new Timer(state => {
                                try
                                {
                                    if (reader != null)
                                    {
                                        reader.SetGpo(gpoPort, false);
                                    }
                                }
                                catch (Exception) { /* Ignorar erros de timer */ }
                            }, null, 500, Timeout.Infinite);

                            Console.WriteLine($"GPO {gpoPort} error signal sent (toggled) on {readerRole} reader");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error setting GPO {gpoPort} on {readerRole} reader: {ex.Message}");
                        }
                    }

                    // Remover do rastreamento
                    gpiEventTimers.TryRemove(eventId, out _);
                    gpiEventVerified.TryRemove(eventId, out _);
                    gpiEventReaders.TryRemove(eventId, out _);
                    gpiEventReaderRoles.TryRemove(eventId, out _);
                    gpiEventGpoEnabled.TryRemove(eventId, out _);
                    gpiEventGpoPorts.TryRemove(eventId, out _);

                    // Atualizar métricas
                    lock (status)
                    {
                        status.Metrics["GpiEventsMissingTag"] = (int)status.Metrics.GetValueOrDefault("GpiEventsMissingTag", 0) + 1;
                    }
                }
            }
        }

        // Método para registrar contagens de sucesso em intervalos regulares
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

                double avgWriteTimeMs = swWriteTimers.Count > 0 ? swWriteTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0;
                double avgVerifyTimeMs = swVerifyTimers.Count > 0 ? swVerifyTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0;
                double avgLockTimeMs = swLockTimers.Count > 0 ? swLockTimers.Values.Average(sw => sw.ElapsedMilliseconds) : 0;
                double readRate = TagOpController.Instance.GetReadRate();

                lock (status)
                {
                    status.TotalTagsProcessed = totalReadCount;
                    status.SuccessCount = successCount;
                    status.FailureCount = totalReadCount - successCount;
                    status.ProgressPercentage = totalReadCount > 0
                        ? Math.Min(100, (double)successCount / totalReadCount * 100)
                        : 0;

                    // Adicionar status de bloqueio e GPI às métricas
                    status.Metrics["LockedTags"] = lockedCount;
                    status.Metrics["LockEnabled"] = enableLock;
                    status.Metrics["PermalockEnabled"] = enablePermalock;
                    status.Metrics["GpiEventsTotal"] = gpiEventsTotal;
                    status.Metrics["GpiEventsVerified"] = gpiEventsVerified;
                    status.Metrics["GpiEventsMissingTag"] = gpiEventsTotal - gpiEventsVerified;
                    status.Metrics["GpiEventsPending"] = gpiEventsPending;
                    status.Metrics["ActiveRoles"] = activeRoles.Trim();
                    status.Metrics["AvgWriteTimeMs"] = avgWriteTimeMs;
                    status.Metrics["AvgVerifyTimeMs"] = avgVerifyTimeMs;
                    status.Metrics["AvgLockTimeMs"] = avgLockTimeMs;
                    status.Metrics["ReadRate"] = readRate;
                    status.Metrics["ElapsedSeconds"] = runTimer.Elapsed.TotalSeconds;

                    // Atualizar métricas específicas do leitor
                    if (readerMetrics.TryGetValue("writer", out var writerMetrics))
                    {
                        writerMetrics.AvgWriteTimeMs = avgWriteTimeMs;
                        writerMetrics.ReadRate = readRate;
                        writerMetrics.SuccessCount = successCount;
                        writerMetrics.FailureCount = totalReadCount - successCount;
                        writerMetrics.LockedTags = lockedCount;
                    }

                    if (readerMetrics.TryGetValue("verifier", out var verifierMetrics))
                    {
                        verifierMetrics.AvgVerifyTimeMs = avgVerifyTimeMs;
                        verifierMetrics.ReadRate = readRate;
                        verifierMetrics.SuccessCount = successCount;
                        verifierMetrics.FailureCount = totalReadCount - successCount;
                    }
                }

                Console.WriteLine($"[{activeRoles.Trim()}] Total Read [{totalReadCount}] Success count: [{successCount}] Locked/Permalocked: [{lockedCount}] GPI Events: {gpiEventsTotal} (Verified: {gpiEventsVerified}, Missing: {gpiEventsTotal - gpiEventsVerified}, Pending: {gpiEventsPending})");
            }
            catch (Exception ex)
            {
                // Ignorar exceções de timer
                Console.WriteLine($"Error in LogSuccessCount: {ex.Message}");
            }
        }

        // Em OnTagsReportedDetector
        private void OnTagsReportedDetector(ImpinjReader sender, TagReport report)
        {
            if (!hasDetectorRole || report == null || IsCancellationRequested())
                return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                var epcHex = tag.Epc?.ToHexString() ?? string.Empty;

                // Verificar se o TID já foi processado
                if (string.IsNullOrEmpty(tidHex) || TagOpController.Instance.IsTidProcessed(tidHex))
                    continue;

                if (epcHex.Length < 24)
                    continue;

                Console.WriteLine($"Detector: New tag detected. TID: {tidHex}, Current EPC: {epcHex}");

                // Gerar um novo EPC se não existir um já registrado
                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                if (string.IsNullOrEmpty(expectedEpc))
                {
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex, _sku, _companyPrefixLength, _encodingMethod);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"Detector: Assigned new EPC for TID {tidHex}: {expectedEpc}");

                    // Log a detecção da tag
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},N/A,0,0,DetectedNewTag,None,0,0,{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,detector");

                    // Se também temos o papel de writer, acionamos a operação de gravação localmente
                    if (hasWriterRole)
                    {
                        // Usar TriggerWriteAndVerifyWithMetrics para garantir que as métricas sejam atualizadas
                        TagOpController.Instance.TriggerWriteAndVerifyWithMetrics(
                            tag,
                            expectedEpc,
                            writerReader,
                            cancellationToken,
                            swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                            newAccessPassword,
                            true,
                            1);
                    }
                    // Caso contrário, apenas registramos a detecção para processamento distribuído
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
        // Em OnTagsReportedWriter
        private void OnTagsReportedWriter(ImpinjReader sender, TagReport report)
        {
            if (!hasWriterRole || report == null || IsCancellationRequested())
                return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                var epcHex = tag.Epc?.ToHexString() ?? string.Empty;

                // Verificar se o TID já foi processado completamente
                if (string.IsNullOrEmpty(tidHex) || TagOpController.Instance.IsTidProcessed(tidHex))
                    continue;

                if (epcHex.Length < 24)
                    continue;

                // Inicializar contador de ciclos
                cycleCount.TryAdd(tidHex, 0);

                if (cycleCount[tidHex] >= MaxCycles)
                {
                    Console.WriteLine($"Max cycles reached for TID {tidHex}, skipping further processing.");
                    continue;
                }

                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                // Se não existir um EPC esperado, geramos um usando a lógica do writer
                if (string.IsNullOrEmpty(expectedEpc))
                {
                    Console.WriteLine($"Writer: New target TID found: {tidHex} Chip {TagOpController.Instance.GetChipModel(tag)}");
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex, _sku, _companyPrefixLength, _encodingMethod);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"Writer: New tag found. TID: {tidHex}. Assigning new EPC: {epcHex} -> {expectedEpc}");

                    // Log da detecção pela função de writer
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},N/A,0,0,DetectedNewTagByWriter,None,0,0,{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,writer");
                }

                // Se o EPC atual for diferente do esperado, fazemos a gravação
                if (!expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase))
                {
                    // Usar TriggerWriteAndVerifyWithMetrics para capturar métricas
                    TagOpController.Instance.TriggerWriteAndVerifyWithMetrics(
                        tag,
                        expectedEpc,
                        sender,
                        cancellationToken,
                        swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                        newAccessPassword,
                        true,
                        1,
                        hasVerifierRole ? false : true, // Só auto-verificamos se não há papel de verificador
                        3);
                }
                else
                {
                    // Se o EPC já está correto, registramos como verificado
                    if (expectedEpc != null && expectedEpc.Equals(epcHex, StringComparison.OrdinalIgnoreCase))
                    {
                        TagOpController.Instance.HandleVerifiedTagWithMetrics(
                            tag,
                            tidHex,
                            expectedEpc,
                            swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                            swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                            cycleCount,
                            tag,
                            TagOpController.Instance.GetChipModelName(tag),
                            logFile);
                    }
                }
            }
        }

        /// <summary>
        /// Event handler for tag reports from the verifier reader
        /// </summary>
        // Em OnTagsReportedWriter
        // Em OnTagsReportedVerifier
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
                if (epcHex.Length < 24)
                    continue;

                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                // Se não existe EPC esperado (pode acontecer no modo distribuído)
                if (string.IsNullOrEmpty(expectedEpc))
                {
                    // Quando operando apenas como verificador, podemos ver tags processadas pelo detector/writer
                    // Usamos uma abordagem alternativa para lidar com este caso
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex, _sku, _companyPrefixLength, _encodingMethod);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"Verifier (fallback detection): TID {tidHex}. Current EPC: {epcHex}, Expected: {expectedEpc}");

                    // Log desta situação - visto pelo verificador primeiro
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},N/A,0,0,DetectedByVerifierFirst,None,0,0,{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,verifier");
                }

                bool success = expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase);
                var writeStatus = success ? "Success" : "Failure";
                Console.WriteLine($"Verifier - TID {tidHex} - current EPC: {epcHex} Expected EPC: {expectedEpc} Operation Status [{writeStatus}]");

                // Tratar verificação de GPI se habilitado
                if (enableGpiTrigger && gpiEventTimers.Count > 0)
                {
                    // Encontrar o evento GPI não verificado mais recente
                    var unverifiedEvents = gpiEventTimers.Where(e =>
                        !gpiEventVerified.TryGetValue(e.Key, out bool verified) || !verified)
                        .OrderByDescending(e => e.Key)
                        .ToList();

                    if (unverifiedEvents.Any())
                    {
                        // Processar o evento GPI mais recente
                        HandleGpiVerification(unverifiedEvents.First(), tidHex, tag, epcHex, expectedEpc, success);
                    }
                }

                if (success)
                {
                    // Verificar se a tag já foi bloqueada
                    bool alreadyLocked = lockedTags.ContainsKey(tidHex);

                    // Se a tag foi verificada com sucesso e lock/permalock está habilitado, mas ainda não a bloqueamos
                    if ((enableLock || enablePermalock) && !alreadyLocked)
                    {
                        PerformLockOperation(tidHex, tag, newAccessPassword, sender);
                    }
                    else
                    {
                        // Se não estamos bloqueando ou a tag já está bloqueada, apenas registramos o sucesso
                        TagOpController.Instance.RecordResult(tidHex, writeStatus, success);
                        TagOpController.Instance.RecordTagRead(); // Atualizar contador de leituras
                        Console.WriteLine($"Verifier - TID {tidHex} verified successfully. Current EPC: {epcHex} - Written tags registered {TagOpController.Instance.GetSuccessCount()} (TIDs processed)");
                    }
                }
                else if (!string.IsNullOrEmpty(expectedEpc))
                {
                    HandleVerificationFailure(tidHex, tag, epcHex, expectedEpc);
                }
            }
        }

        /// <summary>
        /// Método para lidar com falhas de verificação de tags
        /// </summary>
        /// <param name="tidHex">O TID da tag em formato hexadecimal</param>
        /// <param name="tag">O objeto Tag com informações da tag</param>
        /// <param name="epcHex">O EPC atual lido da tag</param>
        /// <param name="expectedEpc">O EPC esperado para a tag</param>
        private void HandleVerificationFailure(string tidHex, Tag tag, string epcHex, string expectedEpc)
        {
            if (!expectedEpc.Equals(epcHex, StringComparison.InvariantCultureIgnoreCase))
            {
                Console.WriteLine($"Verification mismatch for TID {tidHex}: expected {expectedEpc}, read {epcHex}. Verification failed.");

                // Adicionar entrada às métricas de falha se houver métricas específicas do verifier
                if (readerMetrics.TryGetValue("verifier", out var verifierMetrics))
                {
                    verifierMetrics.FailureCount++;
                }

                // Se também temos papel de writer, tentamos novamente a gravação diretamente
                if (hasWriterRole)
                {
                    try
                    {
                        Console.WriteLine($"Verification failed - retrying write locally (since we have writer role)");

                        // Iniciar um novo timer para esta nova tentativa de escrita
                        var writeTimer = new Stopwatch();
                        writeTimer.Start();
                        swWriteTimers[tidHex] = writeTimer;

                        // Incrementar o contador de ciclos para esta tag
                        cycleCount.AddOrUpdate(tidHex, 1, (key, oldValue) => oldValue + 1);

                        // Verificar se há limite de tentativas e se ainda não o atingimos
                        int currentCycle = cycleCount[tidHex];
                        int maxRetries = 3;  // Valor padrão, pode ser configurável

                        // Interromper retentativas se atingir o máximo
                        if (currentCycle > maxRetries)
                        {
                            Console.WriteLine($"Max retry count ({maxRetries}) reached for TID {tidHex}. Giving up after {currentCycle} attempts.");
                            LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},VerificationFailed,{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},MaxRetriesReached,None,0,{currentCycle},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,verifier");
                            TagOpController.Instance.RecordResult(tidHex, "MaxRetriesReached", false);
                            return;
                        }

                        // Registrar esta tentativa no log
                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},RetryingWrite,{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},RetryAttempt_{currentCycle},None,0,{currentCycle},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,verifier");

                        // Tentar escrever novamente usando o EPC esperado através do leitor writer
                        // Usar um maior número de retentativas para esta operação crítica
                        TagOpController.Instance.TriggerWriteAndVerifyWithMetrics(
                            tag,
                            expectedEpc,
                            writerReader,
                            cancellationToken,
                            writeTimer,
                            newAccessPassword,
                            true,
                            1,
                            false,  // Não fazer auto-verificação, deixar o verificador fazer isso
                            5);     // Aumentar o número de retentativas para esta operação crítica
                    }
                    catch (Exception ex)
                    {
                        // Registrar a exceção mas continuar processando
                        Console.WriteLine($"Error triggering write after verification failure: {ex.Message}");
                        LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},ErrorRetrying,0,0,Error,None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,verifier");

                        // Finalizar como falha se não conseguir retentar
                        TagOpController.Instance.RecordResult(tidHex, "RetryError", false);
                    }
                }
                else
                {
                    // Apenas registramos a falha e esperamos que a instância writer trate dela
                    Console.WriteLine($"Verification failed - waiting for writer instance to handle it");
                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},VerificationFailed,{swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},{swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},NeedsRetry,None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,verifier");

                    // Se estamos operando no modo distribuído, não registramos como falha definitiva ainda,
                    // pois a instância writer pode tratar a falha posteriormente
                    if (!operatingAsVerifierOnly)
                    {
                        TagOpController.Instance.RecordResult(tidHex, "VerificationFailed", false);
                    }
                }
            }
            else
            {
                // Este caso geralmente não deve ocorrer, mas se ocorrer, registramos a inconsistência
                Console.WriteLine($"Warning: Inconsistent verification state for TID {tidHex}, EPC matches but verification failed");
                LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},InconsistentState,{swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},{swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},Warning,None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,verifier");

                // Tratamos como sucesso, já que o EPC corresponde ao esperado
                TagOpController.Instance.RecordResult(tidHex, "Success", true);
                Console.WriteLine($"Verifier - TID {tidHex} verified successfully despite inconsistency. Current EPC: {epcHex} - Written tags registered {TagOpController.Instance.GetSuccessCount()} (TIDs processed)");
            }
        }

        // Método auxiliar para processar eventos GPI
        private void HandleGpiVerification(KeyValuePair<long, Stopwatch> recentEvent, string tidHex, Tag tag, string epcHex, string expectedEpc, bool success)
        {
            long eventId = recentEvent.Key;
            Stopwatch timer = recentEvent.Value;

            // Obter qual leitor acionou o evento
            gpiEventReaders.TryGetValue(eventId, out ImpinjReader eventReader);
            gpiEventReaderRoles.TryGetValue(eventId, out string readerRole);
            gpiEventGpoEnabled.TryGetValue(eventId, out bool gpoEnabled);
            gpiEventGpoPorts.TryGetValue(eventId, out ushort eventGpoPort);

            Console.WriteLine($"Tag detected for GPI event {eventId} after {timer.ElapsedMilliseconds}ms: TID={tidHex}, EPC={epcHex}");

            // Marcar este evento como verificado
            gpiEventVerified[eventId] = true;

            // Definir GPO para sucesso se habilitado
            if (gpoEnabled && eventReader != null)
            {
                try
                {
                    if (success)
                    {
                        // Sinal de sucesso (ligado de forma estável)
                        eventReader.SetGpo(eventGpoPort, true);
                        Console.WriteLine($"GPO {eventGpoPort} success signal sent (ON) on {readerRole} reader");
                    }
                    else
                    {
                        // Sinal de EPC errado (pulso duplo)
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

            // Log com informações de GPI
            //LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{epcHex},{expectedEpc},{epcHex},0,{timer.ElapsedMilliseconds},{success ? "Success" : "Failure"}_{readerRole},None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},True,{timer.ElapsedMilliseconds},verifier");

            // Remover do rastreamento
            gpiEventTimers.TryRemove(eventId, out _);
            gpiEventReaders.TryRemove(eventId, out _);
            gpiEventReaderRoles.TryRemove(eventId, out _);
            gpiEventGpoEnabled.TryRemove(eventId, out _);
            gpiEventGpoPorts.TryRemove(eventId, out _);
        }

        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            // Determinar o papel do leitor
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

                    // Registrar o tempo de escrita para métricas
                    TagOpController.Instance.RecordWriteTime(tidHex, swWriteTimers[tidHex].ElapsedMilliseconds);

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

                        // Se temos um papel de verificador, acionamos a verificação lá
                        if (hasVerifierRole)
                        {
                            TagOpController.Instance.TriggerVerificationRead(
                                result.Tag,
                                verifierReader,
                                cancellationToken,
                                swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()),
                                newAccessPassword);
                        }
                        // Caso contrário, se somos um papel de writer-only sem verificador, fazemos auto-verificação
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

                    // Registrar o tempo de verificação para métricas
                    TagOpController.Instance.RecordVerifyTime(tidHex, swVerifyTimers[tidHex].ElapsedMilliseconds);

                    // Processar resultado da verificação
                    HandleReadOpResult(readResult, tidHex, readerRole);
                }
                else if (result is TagLockOpResult lockResult)
                {
                    // Tratar o resultado da operação de bloqueio
                    HandleLockOpResult(lockResult, tidHex, readerRole);
                }
            }
        }

        private void HandleReadOpResult(TagReadOpResult readResult, string tidHex, string readerRole)
        {
            var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
            if (string.IsNullOrEmpty(expectedEpc))
            {
                expectedEpc = TagOpController.Instance.GetNextEpcForTag(readResult.Tag.Epc.ToHexString(), tidHex, _sku, _companyPrefixLength, _encodingMethod);
            }

            var verifiedEpc = readResult.Tag.Epc?.ToHexString() ?? "N/A";
            var success = verifiedEpc.Equals(expectedEpc, StringComparison.InvariantCultureIgnoreCase);
            var status = success ? "Success" : "Failure";

            // Obter ou criar um temporizador de bloqueio para esta tag
            var lockTimer = swLockTimers.GetOrAdd(tidHex, _ => new Stopwatch());

            // Determinar o status de bloqueio com base em se estamos bloqueando e se o temporizador foi executado
            string lockStatus = "None";
            if (enablePermalock && lockedTags.ContainsKey(tidHex))
                lockStatus = "Permalocked";
            else if (enableLock && lockedTags.ContainsKey(tidHex))
                lockStatus = "Locked";

            // Verificar se esta leitura fazia parte de um evento acionado por GPI
            CheckAndHandleGpiEvent(tidHex, readResult.Tag, verifiedEpc, expectedEpc, success);

            // Registrar operação de tag de leitura/gravação, incluindo informações de GPI e leitor, se aplicável
            LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{readResult.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{status},{lockStatus},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},{readResult.Tag.PeakRssiInDbm},{readResult.Tag.AntennaPortNumber},False,0,{readerRole}");
            TagOpController.Instance.RecordResult(tidHex, status, success);

            // Registrar leitura de tag para cálculo de taxa de leitura
            TagOpController.Instance.RecordTagRead();

            Console.WriteLine($"Verification result for TID {tidHex} on {readerRole} reader: {status}");

            cycleCount.AddOrUpdate(tidHex, 1, (key, oldValue) => oldValue + 1);

            if (!success)
            {
                HandleReadVerificationFailure(tidHex, readResult.Tag, verifiedEpc, expectedEpc, readerRole);
            }
        }

        private void CheckAndHandleGpiEvent(string tidHex, Tag tag, string verifiedEpc, string expectedEpc, bool success)
        {
            if (enableGpiTrigger)
            {
                // Procurar se esta verificação de tag corresponde a um evento GPI recente
                var unverifiedEvents = gpiEventTimers.Where(e =>
                    !gpiEventVerified.TryGetValue(e.Key, out bool verified) || !verified)
                    .OrderByDescending(e => e.Key)
                    .ToList();

                if (unverifiedEvents.Any())
                {
                    var recentEvent = unverifiedEvents.First();
                    long matchingEventId = recentEvent.Key;
                    long gpiVerificationTime = recentEvent.Value.ElapsedMilliseconds;

                    // Obter informações do leitor para este evento
                    gpiEventReaders.TryGetValue(matchingEventId, out ImpinjReader eventReader);
                    gpiEventReaderRoles.TryGetValue(matchingEventId, out string gpiReaderRole);
                    gpiEventGpoEnabled.TryGetValue(matchingEventId, out bool gpoEnabled);
                    gpiEventGpoPorts.TryGetValue(matchingEventId, out ushort eventGpoPort);

                    // Marcar como acionado por GPI e verificado
                    gpiEventVerified[matchingEventId] = true;

                    Console.WriteLine($"Tag read operation completed for GPI event {matchingEventId} on {gpiReaderRole} reader: TID={tidHex}, successful={success}");

                    // Definir sinal GPO com base no resultado da verificação, se habilitado
                    HandleGpoSignal(success, eventReader, eventGpoPort, gpiReaderRole, matchingEventId, gpoEnabled);

                    // Remover do rastreamento
                    gpiEventTimers.TryRemove(matchingEventId, out _);
                    gpiEventReaders.TryRemove(matchingEventId, out _);
                    gpiEventReaderRoles.TryRemove(matchingEventId, out _);
                    gpiEventGpoEnabled.TryRemove(matchingEventId, out _);
                    gpiEventGpoPorts.TryRemove(matchingEventId, out _);
                }
            }
        }

        private void HandleGpoSignal(bool success, ImpinjReader reader, ushort gpoPort, string readerRole, long eventId, bool gpoEnabled)
        {
            if (!gpoEnabled || reader == null)
                return;

            try
            {
                if (success)
                {
                    // Sinal de sucesso (ligado de forma estável por 1 segundo)
                    reader.SetGpo(gpoPort, true);
                    Console.WriteLine($"GPO {gpoPort} set to ON (success) on {readerRole} reader for GPI event {eventId}");

                    // Programar reset de GPO após 1 segundo
                    new Timer(state => {
                        try
                        {
                            if (reader != null)
                            {
                                reader.SetGpo(gpoPort, false);
                                Console.WriteLine($"GPO {gpoPort} reset after success signal on {readerRole} reader");
                            }
                        }
                        catch (Exception) { /* Ignorar erros de timer */ }
                    }, null, 1000, Timeout.Infinite);
                }
                else
                {
                    // Sinal de incompatibilidade de EPC (pulso triplo)
                    reader.SetGpo(gpoPort, true);
                    Thread.Sleep(100);
                    reader.SetGpo(gpoPort, false);
                    Thread.Sleep(100);
                    reader.SetGpo(gpoPort, true);
                    Thread.Sleep(100);
                    reader.SetGpo(gpoPort, false);
                    Thread.Sleep(100);
                    reader.SetGpo(gpoPort, true);
                    Thread.Sleep(100);
                    reader.SetGpo(gpoPort, false);
                    Console.WriteLine($"GPO {gpoPort} triple pulse (EPC mismatch) on {readerRole} reader for GPI event {eventId}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting GPO {gpoPort} on {readerRole} reader: {ex.Message}");
            }
        }

        private void HandleReadVerificationFailure(string tidHex, Tag tag, string verifiedEpc, string expectedEpc, string readerRole)
        {
            // Se temos papel de writer no mesmo instance, tentamos reescrever
            if (hasWriterRole)
            {
                try
                {
                    Console.WriteLine($"Verification failed - retrying write locally (since we have writer role)");
                    TagOpController.Instance.TriggerWriteAndVerifyWithMetrics(
                        tag,
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
                    // Registrar exceção mas continuar processando
                    Console.WriteLine($"Error triggering write after verification failure: {ex.Message}");
                }
            }
            else
            {
                // Registrar a falha e esperar que a instance writer trate dela
                Console.WriteLine($"Verification failed - waiting for writer instance to handle it");
                LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{tag.Epc.ToHexString()},{expectedEpc},VerificationFailed,{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},NeedsRetry,None,0,{cycleCount.GetOrAdd(tidHex, 0)},{tag.PeakRssiInDbm},{tag.AntennaPortNumber},False,0,{readerRole}");
            }
        }

        // Método auxiliar para realizar operações de bloqueio
        private void PerformLockOperation(string tidHex, Tag tag, string accessPassword, ImpinjReader reader)
        {
            // Iniciar a temporização da operação de bloqueio
            var lockTimer = swLockTimers.GetOrAdd(tidHex, _ => new Stopwatch());
            lockTimer.Restart();

            // Realizar operação de permalock ou bloqueio padrão
            if (enablePermalock)
            {
                Console.WriteLine($"Permalocking tag with TID {tidHex}");
                TagOpController.Instance.PermaLockTag(tag, accessPassword, reader);
            }
            else if (enableLock)
            {
                Console.WriteLine($"Locking tag with TID {tidHex}");
                TagOpController.Instance.LockTag(tag, accessPassword, reader);
            }

            // Marcar esta tag como tendo tido uma operação de bloqueio acionada
            lockedTags.TryAdd(tidHex, true);
        }

        // Método auxiliar para processar resultados de operações de bloqueio
        private void HandleLockOpResult(TagLockOpResult lockResult, string tidHex, string readerRole)
        {
            // Obter o temporizador de bloqueio para esta tag
            var lockTimer = swLockTimers.GetOrAdd(tidHex, _ => new Stopwatch());
            lockTimer.Stop();

            bool success = lockResult.Result == LockResultStatus.Success;
            string lockStatus = enablePermalock ? "Permalocked" : "Locked";
            string lockOpStatus = success ? "Success" : "Failure";

            Console.WriteLine($"{lockStatus} operation for TID {tidHex} on {readerRole} reader: {lockOpStatus} in {lockTimer.ElapsedMilliseconds}ms");

            // Se o bloqueio falhou mas o EPC ainda está correto, ainda completamos a operação principal
            var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
            var currentEpc = lockResult.Tag.Epc?.ToHexString() ?? "N/A";

            bool epcCorrect = !string.IsNullOrEmpty(expectedEpc) &&
                              expectedEpc.Equals(currentEpc, StringComparison.InvariantCultureIgnoreCase);

            // Sucesso geral é baseado no EPC estar correto, mesmo se o bloqueio falhar
            if (epcCorrect)
            {
                TagOpController.Instance.RecordResult(tidHex, "Success", true);
                TagOpController.Instance.RecordTagRead(); // Atualizar contador de leituras
            }

            // Atualizar o status de bloqueio no cache do controlador de tags
            UpdateTagLockStatus(tidHex, lockResult.Result, lockStatus);

            // Registrar a operação de bloqueio
            LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{currentEpc},{expectedEpc},{currentEpc},{swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},{swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()).ElapsedMilliseconds},Success,{(success ? lockStatus : lockStatus + "Failed")},{lockTimer.ElapsedMilliseconds},{cycleCount.GetOrAdd(tidHex, 0)},{lockResult.Tag.PeakRssiInDbm},{lockResult.Tag.AntennaPortNumber},False,0,{readerRole}");
        }

        // Método auxiliar para atualizar o status de bloqueio no cache
        private void UpdateTagLockStatus(string tidHex, LockResultStatus result, string lockStatus)
        {
            if (string.IsNullOrEmpty(tidHex) || result != LockResultStatus.Success)
                return;

            // Criar um novo objeto de status de bloqueio
            TagOpController.TagLockStatus lockStatusObj = new TagOpController.TagLockStatus();

            // Configurar os estados de bloqueio com base na operação realizada
            if (lockStatus == "Permalocked")
            {
                lockStatusObj.EpcLockState = TagOpController.LockState.Permalocked;
                lockStatusObj.AccessPasswordLockState = TagOpController.LockState.Permalocked;
            }
            else if (lockStatus == "Locked")
            {
                lockStatusObj.EpcLockState = TagOpController.LockState.Locked;
                lockStatusObj.AccessPasswordLockState = TagOpController.LockState.Locked;
            }

            // Atualizar o cache do controlador de tags
            TagOpController.Instance.UpdateTagLockStatus(tidHex, lockStatusObj);
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