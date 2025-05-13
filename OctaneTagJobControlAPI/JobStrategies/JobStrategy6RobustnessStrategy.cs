using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;

namespace OctaneTagWritingTest.JobStrategies
{
    public class JobStrategy6RobustnessStrategy : BaseTestStrategy
    {
        private const int maxRetries = 5;
        private readonly ConcurrentDictionary<string, int> retryCount = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, Stopwatch> swWriteTimers = new ConcurrentDictionary<string, Stopwatch>();
        private readonly ConcurrentDictionary<string, Stopwatch> swVerifyTimers = new ConcurrentDictionary<string, Stopwatch>();

        public JobStrategy6RobustnessStrategy(string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings)
            : base(hostname, logFile, readerSettings)
        {
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                Console.WriteLine("Starting robustness test (write-verify with retries)...");

                ConfigureReader();

                reader.TagsReported += OnTagsReported;
                reader.TagOpComplete += OnTagOpComplete;
                reader.Start();

                if (!File.Exists(logFile))
                {
                    LogToCsv("Timestamp,TID,Previous_EPC,Expected_EPC,Verified_EPC,WriteTime_ms,VerifyTime_ms,Result,Retries,RSSI,AntennaPort,ChipModel");
                }

                while (!IsCancellationRequested())
                {
                    Thread.Sleep(100);
                }

                Console.WriteLine("\nStopping test...");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error in robustness test: " + ex.Message);
            }
            finally
            {
                CleanupReader();
            }
        }

        private void OnTagsReported(ImpinjReader sender, TagReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (Tag tag in report.Tags)
            {
                string tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                if (TagOpController.Instance.IsTidProcessed(tidHex) || TagOpController.Instance.HasResult(tidHex))
                    continue;

                string epcHex = tag.Epc.ToHexString();
                string expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                if (!string.IsNullOrEmpty(expectedEpc) && expectedEpc.Equals(epcHex, StringComparison.OrdinalIgnoreCase))
                {
                    TagOpController.Instance.HandleVerifiedTag(tag, tidHex, expectedEpc, swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()), retryCount, tag, TagOpController.Instance.GetChipModel(tag), logFile);
                                             
                    continue;
                }

                if (string.IsNullOrEmpty(expectedEpc))
                {
                    
                    Console.WriteLine($" Success count: {TagOpController.Instance.GetSuccessCount()}");
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);
                    Console.WriteLine($"Assigning new EPC: {epcHex} -> {expectedEpc}");
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);

                    TagOpController.Instance.TriggerWriteAndVerify(tag, expectedEpc, reader, cancellationToken, swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword, true);
                    Console.WriteLine($"\nNew target TID found: {tidHex} Chip {TagOpController.Instance.GetChipModel(tag)}");
                }
            }
        }

        private void OnTagOpComplete(ImpinjReader reader, TagOpReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (TagOpResult result in report)
            {
                string tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";
                string expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                if (result is TagWriteOpResult)
                {
                    swWriteTimers[tidHex].Stop();
                    TagOpController.Instance.TriggerVerificationRead(result.Tag, reader, cancellationToken, swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword);
                }
                else if (result is TagReadOpResult readResult)
                {
                    if(swVerifyTimers.ContainsKey(tidHex))
                    {
                        swVerifyTimers[tidHex].Stop();
                    }
                    

                    string verifiedEpc = readResult.Data?.ToHexString() ?? "N/A";
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string resultStatus = verifiedEpc.Equals(expectedEpc, StringComparison.OrdinalIgnoreCase) ? "Success" : "Failure";
                    int retries = retryCount.GetOrAdd(tidHex, 0);

                    if (resultStatus == "Failure" && retries < maxRetries)
                    {
                        retryCount[tidHex] = retries + 1;
                        Console.WriteLine($"Verification failed, retry {retryCount[tidHex]} for TID {tidHex}");
                        TagOpController.Instance.TriggerWriteAndVerify(result.Tag, expectedEpc, reader, cancellationToken, swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword, true);
                    }
                    else
                    {
                        LogToCsv($"{timestamp},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{resultStatus},{retries},RSSI,AntennaPort,ChipModel");
                        TagOpController.Instance.RecordResult(tidHex, resultStatus, resultStatus == "Success");
                        Console.WriteLine($"Success TID: {tidHex} new EPC: {result.Tag.Epc.ToHexString()} - Success count: {TagOpController.Instance.GetSuccessCount()}");
                       
                    }
                }
            }
        }

        private void LogToCsv(string logLine)
        {
            TagOpController.Instance.LogToCsv(logFile, logLine);
        }
    }
}



