using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;
using Org.LLRP.LTK.LLRPV1.Impinj;

namespace OctaneTagWritingTest.JobStrategies
{
    /// <summary>
    /// Dual-reader endurance strategy.
    /// The first reader (writerReader) reads tags, generates or retrieves a new EPC for the read TID,
    /// and sends a write operation. The second reader (verifierReader) monitors tag reads,
    /// comparing each tag’s EPC to its expected value; if a mismatch is found, it triggers a re-write
    /// using the expected EPC.
    /// </summary>
    public class JobStrategy8MultipleReaderEnduranceStrategy : BaseTestStrategy
    {
        private const int MaxCycles = 10000;
        private readonly ConcurrentDictionary<string, int> cycleCount = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, Stopwatch> swWriteTimers = new ConcurrentDictionary<string, Stopwatch>();
        private readonly ConcurrentDictionary<string, Stopwatch> swVerifyTimers = new ConcurrentDictionary<string, Stopwatch>();

        private ImpinjReader detectorReader;
        // Two separate readers: one for writing and one for verifying.
        private ImpinjReader writerReader;
        private ImpinjReader verifierReader;
        private string detectorAddress;
        private string writerAddress;
        private string verifierAddress;

        private Timer successCountTimer;

        public JobStrategy8MultipleReaderEnduranceStrategy(string hostnameDetector, string hostnameWriter, string hostnameVerifier, string logFile, Dictionary<string, ReaderSettings> readerSettings)
            : base(hostnameWriter, logFile, readerSettings)
        {
            detectorAddress = hostnameDetector;
            writerAddress = hostnameWriter;
            verifierAddress = hostnameVerifier;

            detectorReader = new ImpinjReader();
            // Create two separate reader instances.
            writerReader = new ImpinjReader();
            verifierReader = new ImpinjReader();

            // Clean up any previous tag operation state.
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                Console.WriteLine("=== Multiple Reader Endurance Test ===");
                Console.WriteLine("Press 'q' to stop the test and return to menu.");

                // Configure readers.

                try
                {
                    ConfigureDetectorReader();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ConfigureDetectorReader - Error: " + ex.Message);
                    throw;
                }

                try
                {
                    ConfigureWriterReader();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ConfigureWriterReader - Error in dual reader endurance test: " + ex.Message);
                    throw ex;
                }
                try
                {
                    ConfigureVerifierReader();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ConfigureVerifierReader - Error in dual reader endurance test: " + ex.Message);
                    throw ex;
                }


                // Register event handlers.
                detectorReader.TagsReported += OnTagsReportedDetector;
                // Register event handlers for the writer reader.
                writerReader.TagsReported += OnTagsReportedWriter;
                writerReader.TagOpComplete += OnTagOpComplete;

                // Register event handlers for the verifier reader.
                verifierReader.TagsReported += OnTagsReportedVerifier;
                verifierReader.TagOpComplete += OnTagOpComplete;

                // Start readers.
                
                try
                {
                    detectorReader.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("detectorReader - Error in dual reader endurance test: " + ex.Message);
                    throw ex;
                }
                try
                {
                    writerReader.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("writerReader - Error in dual reader endurance test: " + ex.Message);
                    throw ex;
                }
                try
                {
                    verifierReader.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("verifierReader - Error in dual reader endurance test: " + ex.Message);
                    throw ex;
                }
                
                
                

                // Create CSV header if the log file does not exist.
                if (!File.Exists(logFile))
                    LogToCsv("Timestamp,TID,Previous_EPC,Expected_EPC,Verified_EPC,WriteTime_ms,VerifyTime_ms,Result,CycleCount,RSSI,AntennaPort");

                // Initialize and start the timer to log success count every 5 seconds
                successCountTimer = new Timer(LogSuccessCount, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));


                while (!IsCancellationRequested())
                {
                    Thread.Sleep(100);
                }

                Console.WriteLine("\nStopping test...");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in dual reader endurance test: " + ex.Message);
            }
            finally
            {
                CleanupReaders();
            }
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

        private void ConfigureDetectorReader()
        {
            // Load the predefined EPC list if needed.
            EpcListManager.Instance.LoadEpcList("epc_list.txt");

            detectorReader.Connect(detectorAddress);
            detectorReader.ApplyDefaultSettings();

            var detectorSettings = detectorReader.QueryDefaultSettings();
            var detectorReaderSettings = GetSettingsForRole("detector");
            detectorSettings.Report.IncludeFastId = detectorReaderSettings.IncludeFastId;
            detectorSettings.Report.IncludePeakRssi = detectorReaderSettings.IncludePeakRssi;
            detectorSettings.Report.IncludePcBits = true;
            detectorSettings.Report.IncludeAntennaPortNumber = detectorReaderSettings.IncludeAntennaPortNumber;
            detectorSettings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), detectorReaderSettings.ReportMode);
            detectorSettings.RfMode = (uint)detectorReaderSettings.RfMode;

            detectorSettings.Antennas.DisableAll();
            detectorSettings.Antennas.GetAntenna((ushort)detectorReaderSettings.AntennaPort).IsEnabled = true;
            detectorSettings.Antennas.GetAntenna((ushort)detectorReaderSettings.AntennaPort).TxPowerInDbm = detectorReaderSettings.TxPowerInDbm;
            detectorSettings.Antennas.GetAntenna((ushort)detectorReaderSettings.AntennaPort).MaxRxSensitivity = detectorReaderSettings.MaxRxSensitivity;
            detectorSettings.Antennas.GetAntenna((ushort)detectorReaderSettings.AntennaPort).RxSensitivityInDbm = detectorReaderSettings.RxSensitivityInDbm;

            // Use a different antenna port for the reader (e.g., port 2).
            detectorSettings.Antennas.GetAntenna(2).IsEnabled = true;
            detectorSettings.Antennas.GetAntenna(2).TxPowerInDbm = detectorReaderSettings.TxPowerInDbm;
            detectorSettings.Antennas.GetAntenna(2).MaxRxSensitivity = detectorReaderSettings.MaxRxSensitivity;
            detectorSettings.Antennas.GetAntenna(2).RxSensitivityInDbm = detectorReaderSettings.RxSensitivityInDbm;

            detectorSettings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), detectorReaderSettings.SearchMode);
            detectorSettings.Session = (ushort)detectorReaderSettings.Session;

            EnableLowLatencyReporting(detectorSettings, detectorReader);
            detectorReader.ApplySettings(detectorSettings);
        }
        private void ConfigureWriterReader()
        {
            // Load the predefined EPC list.
            EpcListManager.Instance.LoadEpcList("epc_list.txt");

            writerReader.Connect(writerAddress);
            writerReader.ApplyDefaultSettings();

            var writerSettings = writerReader.QueryDefaultSettings();
            var writerReaderSettings = GetSettingsForRole("writer");
            writerSettings.Report.IncludeFastId = writerReaderSettings.IncludeFastId;
            writerSettings.Report.IncludePeakRssi = writerReaderSettings.IncludePeakRssi;
            writerSettings.Report.IncludePcBits = true;
            writerSettings.Report.IncludeAntennaPortNumber = writerReaderSettings.IncludeAntennaPortNumber;
            writerSettings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), writerReaderSettings.ReportMode);
            writerSettings.RfMode = (uint)writerReaderSettings.RfMode;

            writerSettings.Antennas.DisableAll();
            writerSettings.Antennas.GetAntenna((ushort)writerReaderSettings.AntennaPort).IsEnabled = true;
            writerSettings.Antennas.GetAntenna((ushort)writerReaderSettings.AntennaPort).TxPowerInDbm = writerReaderSettings.TxPowerInDbm;
            writerSettings.Antennas.GetAntenna((ushort)writerReaderSettings.AntennaPort).MaxRxSensitivity = writerReaderSettings.MaxRxSensitivity;
            writerSettings.Antennas.GetAntenna((ushort)writerReaderSettings.AntennaPort).RxSensitivityInDbm = writerReaderSettings.RxSensitivityInDbm;

            // Use a different antenna port for the reader (e.g., port 2).
            writerSettings.Antennas.GetAntenna(2).IsEnabled = true;
            writerSettings.Antennas.GetAntenna(2).TxPowerInDbm = writerReaderSettings.TxPowerInDbm;
            writerSettings.Antennas.GetAntenna(2).MaxRxSensitivity = writerReaderSettings.MaxRxSensitivity;
            writerSettings.Antennas.GetAntenna(2).RxSensitivityInDbm = writerReaderSettings.RxSensitivityInDbm;

            writerSettings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), writerReaderSettings.SearchMode);
            writerSettings.Session = (ushort)writerReaderSettings.Session;

            EnableLowLatencyReporting(writerSettings, writerReader);
            writerReader.ApplySettings(writerSettings);
        }

        private void ConfigureVerifierReader()
        {
            verifierReader.Connect(verifierAddress);
            verifierReader.ApplyDefaultSettings();

            var verifierSettings = verifierReader.QueryDefaultSettings();
            var verifierReaderSettings = GetSettingsForRole("verifier");
            verifierSettings.Report.IncludeFastId = verifierReaderSettings.IncludeFastId;
            verifierSettings.Report.IncludePeakRssi = verifierReaderSettings.IncludePeakRssi;
            verifierSettings.Report.IncludePcBits = true;
            //verifierSettings.Report.IncludeEnhancedIntegra = true;
            verifierSettings.Report.IncludeAntennaPortNumber = verifierReaderSettings.IncludeAntennaPortNumber;
            verifierSettings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), verifierReaderSettings.ReportMode);
            verifierSettings.RfMode = (uint)verifierReaderSettings.RfMode;

            verifierSettings.Antennas.DisableAll();
            verifierSettings.Antennas.GetAntenna((ushort)verifierReaderSettings.AntennaPort).IsEnabled = true;
            verifierSettings.Antennas.GetAntenna((ushort)verifierReaderSettings.AntennaPort).TxPowerInDbm = verifierReaderSettings.TxPowerInDbm;
            verifierSettings.Antennas.GetAntenna((ushort)verifierReaderSettings.AntennaPort).MaxRxSensitivity = verifierReaderSettings.MaxRxSensitivity;
            verifierSettings.Antennas.GetAntenna((ushort)verifierReaderSettings.AntennaPort).RxSensitivityInDbm = verifierReaderSettings.RxSensitivityInDbm;
            // Use a different antenna port for the verifier (e.g., port 2).
            verifierSettings.Antennas.GetAntenna(2).IsEnabled = true;
            verifierSettings.Antennas.GetAntenna(2).TxPowerInDbm = verifierReaderSettings.TxPowerInDbm;
            verifierSettings.Antennas.GetAntenna(2).MaxRxSensitivity = verifierReaderSettings.MaxRxSensitivity;
            verifierSettings.Antennas.GetAntenna(2).RxSensitivityInDbm = verifierReaderSettings.RxSensitivityInDbm;

            verifierSettings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), verifierReaderSettings.SearchMode);
            verifierSettings.Session = (ushort)verifierReaderSettings.Session;

            EnableLowLatencyReporting(verifierSettings, verifierReader);
            verifierReader.ApplySettings(verifierSettings);
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

        private void CleanupReaders()
        {
            try
            {
                if (detectorReader != null)
                {
                    detectorReader.Stop();
                    detectorReader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("detectorReader - Error during cleanup: " + ex.Message);
            }
            try
            {
                if (writerReader != null)
                {
                    writerReader.Stop();
                    writerReader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("writerReader - Error during reader cleanup: " + ex.Message);
            }
            try
            {
                if (verifierReader != null)
                {
                    verifierReader.Stop();
                    verifierReader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("verifierReader - Error during reader cleanup: " + ex.Message);
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
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex,tidHex);
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
                Console.WriteLine($"OnTagsReportedVerifier - TID {tidHex} - current EPC: {epcHex} Expected EPC: {expectedEpc} Operation Status [{writeStatus}]" );
                Console.WriteLine(".........................................");

                if (success)
                {
                    TagOpController.Instance.RecordResult(tidHex, writeStatus, success);
                    Console.WriteLine($"OnTagsReportedVerifier - TID {tidHex} verified successfully on verifier reader. Current EPC: {epcHex} - Written tags regitered {TagOpController.Instance.GetSuccessCount()} (TIDs processed)");
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
                        Console.WriteLine($"OnTagsReportedVerifier - TID {tidHex} verified successfully on verifier reader. Current EPC: {epcHex} - Written tags regitered {TagOpController.Instance.GetSuccessCount()} (TIDs processed)");
                    }
                }
            }
        }

        /// <summary>
        /// Common event handler for tag operation completions from both readers.
        /// Processes write and read operations, logs the result, and updates the cycle count.
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

                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{status},{cycleCount.GetOrAdd(tidHex, 0)},RSSI,AntennaPort");
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

                        }
                    }

                    
                }
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
