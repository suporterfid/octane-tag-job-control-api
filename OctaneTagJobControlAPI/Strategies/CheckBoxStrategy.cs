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
using OctaneTagWritingTest.Helpers;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Strategies.Base.Configuration;

namespace OctaneTagJobControlAPI.Strategies
{
    [StrategyDescription(
        "Reads, encodes, and writes EPC data to tags in a checkbox-like flow controlled by GPI events",
        "Encoding",
        StrategyCapability.Reading | StrategyCapability.Writing | StrategyCapability.Verification | StrategyCapability.Encoding)]
    public class CheckBoxStrategy : SingleReaderStrategyBase
    {
        public enum EpcEncodingMethod
        {
            BasicWithTidSuffix,
            SGTIN96,
            CustomFormat
        }

        private const int ReadDurationSeconds = 10;
        private const int WriteTimeoutSeconds = 20;
        private const int VerificationDurationMs = 5000;

        private readonly string _sku;
        private readonly string _epcHeader;
        private readonly EpcEncodingMethod _encodingMethod;
        private readonly int _partitionValue;
        private readonly int _itemReference;

        // Thread-safe dictionary to track all tag operations
        private readonly ConcurrentDictionary<string, TagOperationData> _tagOperations 
            = new ConcurrentDictionary<string, TagOperationData>();

        // Thread-safe counter for verified tags
        private int _successCount;

        // Lock object for status updates
        private readonly object _statusLock = new object();

        // Dictionary for capturing tags during verification phase
        private readonly ConcurrentDictionary<string, Tag> _verificationTags = new ConcurrentDictionary<string, Tag>();

        private int _gpiProcessingFlag = 0;
        private bool _isCollectingTags = true;
        private bool _isVerificationPhase = false;
        private readonly Stopwatch _runTimer = new Stopwatch();
        private JobExecutionStatus _status = new JobExecutionStatus();

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
            : base(hostname, logFile, readerSettings, serviceProvider)
        {
            _epcHeader = epcHeader;
            _sku = sku ?? "012345678901";

            if (Enum.TryParse<EpcEncodingMethod>(encodingMethod, true, out var method))
            {
                _encodingMethod = method;
            }
            else
            {
                _encodingMethod = EpcEncodingMethod.BasicWithTidSuffix;
                Console.WriteLine($"Unrecognized encoding method '{encodingMethod}', defaulting to BasicWithTidSuffix");
            }

            if (_encodingMethod == EpcEncodingMethod.BasicWithTidSuffix && _sku.Length != 12)
            {
                Console.WriteLine("Warning: SKU should be 12 digits for BasicWithTidSuffix encoding");
            }

            _partitionValue = Math.Clamp(partitionValue, 0, 6);
            _itemReference = itemReference;
            _status.CurrentOperation = "Initialized";
            TagOpController.Instance.CleanUp();
        }

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

                EpcListManager.Instance.LoadEpcList("epc_list.txt");

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

                if (!File.Exists(logFile))
                    LogToCsv("Timestamp,TID,Original_EPC,Expected_EPC,Verified_EPC,Encoding,Result");

                _status.CurrentOperation = "Waiting for GPI Trigger";

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).KeyChar == 'q')
                    {
                        break;
                    }

                    Thread.Sleep(200);
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

        public override JobExecutionStatus GetStatus()
        {
            lock (_status)
            {
                var totalTags = _tagOperations.Count;
                var verifiedTags = _tagOperations.Values.Count(t => t.IsVerified);
                
                return new JobExecutionStatus
                {
                    TotalTagsProcessed = totalTags,
                    SuccessCount = verifiedTags,
                    FailureCount = totalTags - verifiedTags,
                    ProgressPercentage = totalTags > 0 ? (double)verifiedTags / totalTags * 100 : 0,
                    CurrentOperation = _status.CurrentOperation,
                    RunTime = _status.RunTime,
                    Metrics = new Dictionary<string, object>
                    {
                        { "EncodingMethod", _encodingMethod.ToString() },
                        { "CollectedTags", totalTags },
                        { "VerifiedTags", verifiedTags },
                        { "ElapsedSeconds", _runTimer.Elapsed.TotalSeconds },
                        { "SKU", _sku },
                        { "EpcHeader", _epcHeader }
                    }
                };
            }
        }

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

            var gpi = settingsToApply.Gpis.GetGpi(1);
            gpi.IsEnabled = true;
            gpi.DebounceInMs = 50;

            settingsToApply.AutoStart.Mode = AutoStartMode.GpiTrigger;
            settingsToApply.AutoStart.GpiPortNumber = 1;
            settingsToApply.AutoStart.GpiLevel = true;
            settingsToApply.AutoStop.Mode = AutoStopMode.GpiTrigger;
            settingsToApply.AutoStop.GpiPortNumber = 1;
            settingsToApply.AutoStop.GpiLevel = false;

            reader.GpiChanged += OnGpiEvent;
            reader.TagsReported += OnTagsReported;
            reader.TagOpComplete += OnTagOpComplete;

            EnableLowLatencyReporting(settingsToApply);

            reader.ApplySettings(settingsToApply);
            reader.Start();
        }

        private async void OnGpiEvent(ImpinjReader sender, GpiEvent e)
        {
            if (e.PortNumber != 1)
                return;

            if (e.State)
            {
                if (Interlocked.CompareExchange(ref _gpiProcessingFlag, 1, 0) == 0)
                {
                    Console.WriteLine("GPI Port 1 is TRUE - initiating tag collection and processing.");
                    _status.CurrentOperation = "Collecting Tags";

                    bool collectionConfirmed = await WaitForReadTagsAsync();
                    if (collectionConfirmed)
                    {
                        _status.CurrentOperation = "Writing Tags";
                        await EncodeReadTagsAsync();

                        _status.CurrentOperation = "Verifying Tags";
                        await VerifyWrittenTagsAsync();

                        _status.CurrentOperation = "Cycle Complete";
                    }
                    else
                    {
                        _status.CurrentOperation = "Cancelled by User";
                    }
                }
                else
                {
                    Console.WriteLine("GPI Port 1 event received while processing already in progress. Ignoring duplicate trigger.");
                }
            }
            else
            {
                Console.WriteLine("GPI Port 1 is FALSE - resetting processing flag.");
                CleanupTags();
                Interlocked.Exchange(ref _gpiProcessingFlag, 0);
                _status.CurrentOperation = "Waiting for GPI Trigger";
            }
        }

        private async Task<bool> WaitForReadTagsAsync()
        {
            _isCollectingTags = true;
            _tagOperations.Clear();

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

            _isCollectingTags = false;

            lock (_status)
            {
                _status.TotalTagsProcessed = _tagOperations.Count;
            }

            Console.WriteLine("Tag collection ended. Total tags collected: {0}. Confirm? (y/n)", _tagOperations.Count);
            string confirmation = Console.ReadLine();
            if (!confirmation.Equals("y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Operation canceled by user.");
                return false;
            }

            return true;
        }

        private async Task EncodeReadTagsAsync()
        {
            Console.WriteLine($"Starting write phase using {_encodingMethod} encoding...");
            Stopwatch globalWriteTimer = Stopwatch.StartNew();
            Stopwatch swWrite = new Stopwatch();

            foreach (var kvp in _tagOperations)
            {
                if (globalWriteTimer.Elapsed.TotalSeconds > WriteTimeoutSeconds)
                {
                    Console.WriteLine("Global write timeout reached.");
                    break;
                }
                
                var tagOperation = kvp.Value;

                string newEpc = GenerateEpc(tagOperation.Tag);
                TagOpController.Instance.RecordExpectedEpc(tagOperation.TID, newEpc);

                TagOpController.Instance.TriggerWriteAndVerify(
                    tagOperation.Tag,
                    newEpc,
                    reader,
                    cancellationToken,
                    swWrite,
                    newAccessPassword,
                    true,
                    1,
                    true,
                    3);

                await Task.Delay(100, cancellationToken);
                tagOperation.ExpectedEPC = newEpc;
            }

            globalWriteTimer.Stop();
        }

        private string GenerateEpc(Tag tag)
        {
            string tid = tag.Tid.ToHexString();

            switch (_encodingMethod)
            {
                case EpcEncodingMethod.SGTIN96:
                    try
                    {
                        string gtin = _sku;
                        if (gtin.Length < 13)
                        {
                            gtin = gtin.PadLeft(13, '0');
                        }

                        var sgtin96 = Sgtin96.FromGTIN(gtin, _partitionValue);
                        string serialStr = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid;

                        if (ulong.TryParse(serialStr, System.Globalization.NumberStyles.HexNumber, null, out ulong serialNumber))
                        {
                            serialNumber = Math.Min(serialNumber, 274877906943);
                        }
                        else
                        {
                            serialNumber = (ulong)Math.Abs(tid.GetHashCode()) % 274877906943;
                        }

                        sgtin96.SerialNumber = serialNumber;
                        string newEpc = sgtin96.ToEpc();

                        Console.WriteLine($"Generated SGTIN-96 EPC for TID {tid}: {newEpc}");
                        return newEpc;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error generating SGTIN-96 EPC: {ex.Message}. Falling back to basic encoding.");
                        return GenerateBasicEpcWithTidSuffix(tid);
                    }

                case EpcEncodingMethod.CustomFormat:
                    Console.WriteLine("CustomFormat encoding not yet implemented, falling back to basic encoding.");
                    return GenerateBasicEpcWithTidSuffix(tid);

                case EpcEncodingMethod.BasicWithTidSuffix:
                default:
                    return GenerateBasicEpcWithTidSuffix(tid);
            }
        }

        private string GenerateBasicEpcWithTidSuffix(string tid)
        {
            string tidSuffix = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
            string newEpc = _epcHeader + _sku + tidSuffix;
            Console.WriteLine($"Generated basic EPC for TID {tid}: {newEpc}");
            return newEpc;
        }

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

                    var tagOperation = new TagOperationData
                    {
                        TID = tid,
                        OriginalEPC = epc,
                        AntennaPort = tag.AntennaPortNumber,
                        RSSI = tag.PeakRssiInDbm,
                        EncodingMethod = _encodingMethod.ToString(),
                        Tag = tag
                    };
                    _tagOperations.TryAdd(tid, tagOperation);

                    Console.WriteLine("Detected Tag: TID: {0}, EPC: {1}, Antenna: {2}", tid, epc, tag.AntennaPortNumber);

                    lock (_status)
                    {
                        _status.TotalTagsProcessed = _tagOperations.Count;
                    }
                }
            }
        }

        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || cancellationToken.IsCancellationRequested)
                return;

            foreach (TagOpResult result in report)
            {
                string tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";

                if (result is TagWriteOpResult writeResult)
                {
                    string resultStatus = writeResult.Result.ToString();
                    Console.WriteLine($"Write operation for TID {tidHex}: {resultStatus}");

                    if (resultStatus == "Success" && _tagOperations.TryGetValue(tidHex, out var tagOperation))
                    {
                        tagOperation.IsVerified = true;
                        _successCount++;

                        lock (_status)
                        {
                            _status.SuccessCount = _successCount;
                        }
                    }
                }
            }
        }

        private async Task VerifyWrittenTagsAsync()
        {
            Console.WriteLine("Starting verification phase...");

            _verificationTags.Clear();
            _isVerificationPhase = true;

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

                if (_tagOperations.TryGetValue(tid, out var tagOperation))
                {
                    string expectedEpc = tagOperation.ExpectedEPC;
                    bool success = string.Equals(reportedEpc, expectedEpc, StringComparison.OrdinalIgnoreCase);
                    
                    if (success)
                    {
                        verifiedCount++;
                        tagOperation.IsVerified = true;
                        Console.WriteLine($"Verification SUCCESS: TID {tid} reported EPC {reportedEpc}");
                    }
                    else
                    {
                        Console.WriteLine($"Verification FAILURE: TID {tid} expected EPC {expectedEpc} but got {reportedEpc}");
                    }

                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tid},{tagOperation.OriginalEPC},{expectedEpc},{reportedEpc},{_encodingMethod},{(success ? "Success" : "Failure")}");

                    if (!TagOpController.Instance.IsTidProcessed(tid))
                    {
                        TagOpController.Instance.RecordResult(tid, success ? "Success" : "Failure", success);
                    }
                }
                else
                {
                    Console.WriteLine($"Verification: No data recorded for TID {tid}");
                }
            }

            lock (_status)
            {
                _status.SuccessCount = verifiedCount;
                _status.FailureCount = _tagOperations.Count - verifiedCount;
                _status.ProgressPercentage = _tagOperations.Count > 0
                    ? (double)verifiedCount / _tagOperations.Count * 100
                    : 0;
            }

            Console.WriteLine($"Verification complete: {verifiedCount} / {_tagOperations.Count} tags verified successfully.");
        }

        private void CleanupTags()
        {
            try
            {
                Console.WriteLine("Cleaning up tag collections...");
                _verificationTags.Clear();
                _isCollectingTags = false;
                _isVerificationPhase = false;
                Console.WriteLine("Tag collections cleared.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during tag cleanup: " + ex.Message);
            }
        }

        public override void Dispose()
        {
            CleanupTags();
            base.Dispose();
        }
    }
}
