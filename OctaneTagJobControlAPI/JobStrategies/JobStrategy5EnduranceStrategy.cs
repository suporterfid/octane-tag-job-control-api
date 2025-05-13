using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;

namespace OctaneTagWritingTest.JobStrategies
{
    public class JobStrategy5EnduranceStrategy : BaseTestStrategy
    {
        private const int MaxCycles = 10000;
        private readonly ConcurrentDictionary<string, int> cycleCount = new();
        private readonly ConcurrentDictionary<string, Stopwatch> swWriteTimers = new();
        private readonly ConcurrentDictionary<string, Stopwatch> swVerifyTimers = new();
        private Timer successCountTimer;

        public JobStrategy5EnduranceStrategy(string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings) : base(hostname, logFile, readerSettings)
        {
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                Console.WriteLine("=== Endurance Test ===");
                Console.WriteLine("Press 'q' to stop the test and return to menu.");

                ConfigureReader();

                reader.TagsReported += OnTagsReported;
                reader.TagOpComplete += OnTagOpComplete;

                reader.Start();

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
                Console.WriteLine("Error in endurance test: " + ex.Message);
            }
            finally
            {
                CleanupReader();
                successCountTimer?.Dispose();
            }
        }

        private void LogSuccessCount(object state)
        {
            try
            {
                int successCount = TagOpController.Instance.GetSuccessCount();
                Console.WriteLine($"!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine($"!!!!!!!!!!!!!!!!!!!!!!!!!! Success count: [{successCount}] !!!!!!!!!!!!!!!!!!!!!!!!!!");
                Console.WriteLine($"!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            }
            catch (Exception)
            {
            }
            
        }

        private void LogFailure(string tidHex, string reason, Tag tag)
        {
            LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{tag.Epc.ToHexString()},N/A,N/A,{swWriteTimers[tidHex].ElapsedMilliseconds},0,{reason},{cycleCount[tidHex]},RSSI,AntennaPort");
            TagOpController.Instance.RecordResult(tidHex, reason, false);
        }

        private void OnTagsReported(ImpinjReader sender, TagReport report)
        {
            if (report == null || IsCancellationRequested()) return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                if (string.IsNullOrEmpty(tidHex) || TagOpController.Instance.IsTidProcessed(tidHex))
                    continue;

                cycleCount.TryAdd(tidHex, 0);

                if (cycleCount[tidHex] >= MaxCycles)
                {
                    Console.WriteLine($"Max cycles reached for TID {tidHex}, skipping further processing.");
                    continue;
                }

                var epcHex = tag.Epc.ToHexString();
                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                if (expectedEpc != null && expectedEpc.Equals(epcHex, StringComparison.OrdinalIgnoreCase))
                {
                    TagOpController.Instance.HandleVerifiedTag(tag, tidHex, expectedEpc, swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()), cycleCount, tag, TagOpController.Instance.GetChipModel(tag), logFile);
                    continue;
                }

                if (string.IsNullOrEmpty(expectedEpc))
                {
                    Console.WriteLine($" Success count: {TagOpController.Instance.GetSuccessCount()}");
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"New target TID found: {tidHex} Chip {TagOpController.Instance.GetChipModel(tag)}");
                }

                TagOpController.Instance.TriggerWriteAndVerify(tag, expectedEpc, reader, cancellationToken, swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword, true);
            }
        }

        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || IsCancellationRequested()) return;

            foreach (TagOpResult result in report)
            {
                var tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";

                if (result is TagWriteOpResult writeResult)
                {
                    swWriteTimers[tidHex].Stop();

                    if (writeResult.Result == WriteResultStatus.Success)
                    {
                        Console.WriteLine($"WriteResultStatus {tidHex} - Success count: {TagOpController.Instance.GetSuccessCount()} {result.Tag.AntennaPortNumber}");
                        TagOpController.Instance.TriggerVerificationRead(result.Tag, reader, cancellationToken, swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword);
                    
                    }
                    else
                    {
                        LogFailure(tidHex, "Write failure", result.Tag);
                        Console.WriteLine($"WriteResultStatus {tidHex}- Write failure. {result.Tag.AntennaPortNumber}");
                    }
                }
                else if (result is TagReadOpResult readResult)
                {
                    swVerifyTimers[tidHex].Stop();

                    var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    var verifiedEpc = readResult.Tag.Epc?.ToHexString() ?? "N/A";
                    var success = verifiedEpc.Equals(expectedEpc, StringComparison.OrdinalIgnoreCase);
                    var status = success ? "Success" : "Failure";

                    LogToCsv($"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{status},{cycleCount[tidHex]},RSSI,AntennaPort");
                    TagOpController.Instance.RecordResult(tidHex, status, success);
                    Console.WriteLine($"WriteResultStatus {tidHex} - Status {status} Success count: {TagOpController.Instance.GetSuccessCount()} Port:  {result.Tag.AntennaPortNumber}");

                    cycleCount[tidHex]++;
                }
            }
        }



        private void LogToCsv(string logLine)
        {
            TagOpController.Instance.LogToCsv(logFile, logLine);
        }
    }
}


