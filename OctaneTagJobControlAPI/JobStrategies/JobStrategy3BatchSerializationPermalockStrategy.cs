using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;

namespace OctaneTagWritingTest.JobStrategies
{
    public class JobStrategy3BatchSerializationPermalockStrategy : BaseTestStrategy
    {
        private readonly ConcurrentDictionary<string, Stopwatch> writeTimers = new();
        private readonly ConcurrentQueue<Tag> tagsQueue = new();

        public JobStrategy3BatchSerializationPermalockStrategy(string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings)
            : base(hostname, logFile, readerSettings)
        {
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;

                ConfigureReader();

                reader.TagsReported += OnTagsReported;
                reader.TagOpComplete += OnTagOpComplete;
                reader.Start();

                if (!File.Exists(logFile))
                {
                    LogToCsv("Timestamp,TID,OldEPC,NewEPC,SerialCounter,WriteTime,Result,RSSI,AntennaPort");
                }

                while (!IsCancellationRequested())
                {
                    ProcessTags();
                    Thread.Sleep(100);
                }
            }
            finally
            {
                CleanupReader();
            }
        }

        private void OnTagsReported(ImpinjReader sender, TagReport report)
        {
            if (report == null || IsCancellationRequested()) return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;

                if (TagOpController.Instance.HasResult(tidHex) || TagOpController.Instance.IsTidProcessed(tidHex))
                    continue;

                tagsQueue.Enqueue(tag);
            }
        }

        private void ProcessTags()
        {
            while (tagsQueue.TryDequeue(out var tag))
            {
                if (IsCancellationRequested()) return;

                var tidHex = tag.Tid.ToHexString();
                string epcHex = tag.Epc?.ToHexString() ?? string.Empty;
                //var currentEpc = tag.Epc.ToHexString();
                var newEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);

                TagOpController.Instance.RecordExpectedEpc(tidHex, newEpc);

                Console.WriteLine($"Batch writing EPC {newEpc} to tag {tidHex}");

                var swWrite = writeTimers.GetOrAdd(tidHex, _ => new Stopwatch());

                TagOpController.Instance.TriggerWriteAndVerify(tag, newEpc, reader, cancellationToken, swWrite, newAccessPassword, true);
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
                    var swWrite = writeTimers.GetOrAdd(tidHex, _ => new Stopwatch());
                    swWrite.Stop();

                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var oldEpc = writeResult.Tag.Epc.ToHexString();
                    var newEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    var res = writeResult.Result.ToString();
                    var rssi = writeResult.Tag.IsPeakRssiInDbmPresent ? writeResult.Tag.PeakRssiInDbm : 0;
                    var antennaPort = writeResult.Tag.IsAntennaPortNumberPresent ? writeResult.Tag.AntennaPortNumber : (ushort)0;
                    var writeTime = swWrite.ElapsedMilliseconds;

                    var wasSuccess = res == "Success";

                    LogToCsv($"{timestamp},{tidHex},{oldEpc},{newEpc},{TagOpController.Instance.GetSuccessCount()},{writeTime},{res},{rssi},{antennaPort}");

                    TagOpController.Instance.RecordResult(tidHex, res, wasSuccess);

                    Console.WriteLine($"Write complete for TID={tidHex}: Result={res}, Time={writeTime}ms");
                }
            }
        }

        private void LogToCsv(string logLine)
        {
            TagOpController.Instance.LogToCsv(logFile, logLine);
        }
    }
}
