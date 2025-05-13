using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;

namespace OctaneTagWritingTest.JobStrategies
{
    public class JobStrategy7OptimizedStrategy : BaseTestStrategy
    {
        private readonly ConcurrentDictionary<string, int> retryCount = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, Stopwatch> swWriteTimers = new ConcurrentDictionary<string, Stopwatch>();
        private readonly ConcurrentDictionary<string, Stopwatch> swVerifyTimers = new ConcurrentDictionary<string, Stopwatch>();
        private const int maxRetries = 3;

        public JobStrategy7OptimizedStrategy(string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings)
            : base(hostname, logFile, readerSettings)
        {
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                Console.WriteLine("Starting optimized inline write, permalock, and verification strategy...");

                ConfigureReader();

                reader.TagsReported += OnTagsReported;
                reader.TagOpComplete += OnTagOpComplete;
                reader.Start();

                if (!File.Exists(logFile))
                {
                    TagOpController.Instance.LogToCsv(logFile, "Timestamp,TID,OldEPC,NewEPC,VerifiedEPC,WriteTime_ms,VerifyTime_ms,Result,Retries,RSSI,AntennaPort,ChipModel");
                }

                while (!IsCancellationRequested())
                {
                    Thread.Sleep(100);
                }

                Console.WriteLine("\nStopping test...");
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

            foreach (var tag in report.Tags)
            {
                string tidHex = tag.Tid?.ToHexString() ?? string.Empty;

                if (string.IsNullOrEmpty(tidHex) || TagOpController.Instance.HasResult(tidHex))
                    continue;

                if (!TagOpController.Instance.IsTidProcessed(tidHex))
                {
                    string epcHex = tag.Epc.ToHexString();
                    string expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);

                    Console.WriteLine($"New tag found. TID: {tidHex}. Assigning new EPC: {epcHex} -> {expectedEpc}");
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);

                    TagOpController.Instance.TriggerWriteAndVerify(tag, expectedEpc, reader, cancellationToken,
                        swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword, true);
                }
            }
        }

        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (TagOpResult result in report)
            {
                string tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";

                if (result is TagWriteOpResult writeResult)
                {
                    swWriteTimers[tidHex].Stop();

                    if (writeResult.Result == WriteResultStatus.Success)
                    {
                        Console.WriteLine($"Write successful for TID: {tidHex}, initiating verification.");
                        TagOpController.Instance.TriggerVerificationRead(writeResult.Tag, reader, cancellationToken,
                            swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword);
                    }
                    else
                    {
                        LogResult(tidHex, writeResult.Tag.Epc.ToHexString(), TagOpController.Instance.GetExpectedEpc(tidHex), "N/A",
                                  "Write Failure", retryCount.GetOrAdd(tidHex, 0), writeResult.Tag);
                        TagOpController.Instance.RecordResult(tidHex, "Write Error", false);
                    }
                }
                else if (result is TagReadOpResult readResult)
                {
                    swVerifyTimers[tidHex].Stop();

                    string verifiedEpc = readResult.Data?.ToHexString() ?? "N/A";
                    string expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    string resultStatus = verifiedEpc.Equals(expectedEpc, StringComparison.OrdinalIgnoreCase) ? "Success" : "Verification Failure";

                    int retries = retryCount.GetOrAdd(tidHex, 0);

                    if (resultStatus == "Verification Failure" && retries < maxRetries)
                    {
                        retryCount[tidHex] = retries + 1;
                        Console.WriteLine($"Verification failed, retry {retryCount[tidHex]} for TID {tidHex}");

                        TagOpController.Instance.TriggerWriteAndVerify(readResult.Tag, expectedEpc, reader, cancellationToken,
                            swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword, true);
                    }
                    else
                    {
                        LogResult(tidHex, readResult.Tag.Epc.ToHexString(), expectedEpc, verifiedEpc,
                                  resultStatus, retries, readResult.Tag);
                        TagOpController.Instance.RecordResult(tidHex, resultStatus, resultStatus == "Success");
                    }
                }
            }
        }

        private void LogResult(string tidHex, string oldEpc, string newEpc, string verifiedEpc, string result, int retries, Tag tag)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            long writeTime = swWriteTimers.ContainsKey(tidHex) ? swWriteTimers[tidHex].ElapsedMilliseconds : 0;
            long verifyTime = swVerifyTimers.ContainsKey(tidHex) ? swVerifyTimers[tidHex].ElapsedMilliseconds : 0;
            double rssi = tag.IsPeakRssiInDbmPresent ? tag.PeakRssiInDbm : 0;
            ushort antennaPort = tag.IsAntennaPortNumberPresent ? tag.AntennaPortNumber : (ushort)0;
            string chipModel = TagOpController.Instance.GetChipModel(tag);

            string logLine = $"{timestamp},{tidHex},{oldEpc},{newEpc},{verifiedEpc},{writeTime},{verifyTime},{result},{retries},{rssi},{antennaPort},{chipModel}";
            TagOpController.Instance.LogToCsv(logFile, logLine);
        }
    }
}



