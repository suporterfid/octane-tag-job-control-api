using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;

namespace OctaneTagWritingTest.JobStrategies
{
    public class JobStrategy4VerificationCycleStrategy : BaseTestStrategy
    {
        private readonly ConcurrentDictionary<string, Stopwatch> swWriteTimers = new ConcurrentDictionary<string, Stopwatch>();
        private readonly ConcurrentDictionary<string, Stopwatch> swVerifyTimers = new ConcurrentDictionary<string, Stopwatch>();

        public JobStrategy4VerificationCycleStrategy(string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings)
            : base(hostname, logFile, readerSettings)
        {
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                Console.WriteLine("Starting verification cycle test (write-verify)...");

                ConfigureReader();

                reader.TagsReported += OnTagsReported;
                reader.TagOpComplete += OnTagOpComplete;
                reader.Start();

                if (!File.Exists(logFile))
                {
                    LogToCsv("Timestamp,TID,Previous_EPC,Expected_EPC,Verified_EPC,WriteTime_ms,VerifyTime_ms,Result,RSSI,AntennaPort");
                }

                while (!IsCancellationRequested())
                {
                    Thread.Sleep(100);
                }

                Console.WriteLine("\nStopping test...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in verification test: {ex.Message}");
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
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;

                if (TagOpController.Instance.IsTidProcessed(tidHex) || TagOpController.Instance.HasResult(tidHex))
                    continue;

                var epcHex = tag.Epc.ToHexString();
                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                if (!string.IsNullOrEmpty(expectedEpc) && expectedEpc.Equals(epcHex, StringComparison.OrdinalIgnoreCase))
                {
                    TagOpController.Instance.RecordResult(tidHex, epcHex, true);
                    continue;
                }

                if (string.IsNullOrEmpty(expectedEpc))
                {
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);

                    TagOpController.Instance.TriggerWriteAndVerify(tag, expectedEpc, reader, cancellationToken,
                        swWriteTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword, true);
                }
            }
        }

        private void OnTagOpComplete(ImpinjReader reader, TagOpReport report)
        {
            if (report == null || IsCancellationRequested())
                return;

            foreach (TagOpResult result in report)
            {
                var tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";
                var expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                if (result is TagWriteOpResult)
                {
                    swWriteTimers[tidHex].Stop();
                    TagOpController.Instance.TriggerVerificationRead(result.Tag, reader, cancellationToken,
                        swVerifyTimers.GetOrAdd(tidHex, _ => new Stopwatch()), newAccessPassword);
                }
                else if (result is TagReadOpResult readResult)
                {
                    swVerifyTimers[tidHex].Stop();

                    var verifiedEpc = readResult.Data?.ToHexString() ?? "N/A";
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var resultStatus = verifiedEpc.Equals(expectedEpc, StringComparison.OrdinalIgnoreCase) ? "Success" : "Failure";
                    var resultRssi = readResult.Tag.IsPcBitsPresent ? readResult.Tag.PeakRssiInDbm : 0;
                    var antennaPort = readResult.Tag.IsAntennaPortNumberPresent ? readResult.Tag.AntennaPortNumber : (ushort)0;

                    LogToCsv($"{timestamp},{tidHex},{result.Tag.Epc.ToHexString()},{expectedEpc},{verifiedEpc},{swWriteTimers[tidHex].ElapsedMilliseconds},{swVerifyTimers[tidHex].ElapsedMilliseconds},{resultStatus},{resultRssi},{antennaPort}");

                    TagOpController.Instance.RecordResult(tidHex, resultStatus, resultStatus == "Success");
                }
            }
        }

        private void LogToCsv(string logLine)
        {
            TagOpController.Instance.LogToCsv(logFile, logLine);
        }
    }
}



