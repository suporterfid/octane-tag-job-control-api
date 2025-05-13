using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OctaneTagWritingTest.JobStrategies
{
    public class JobStrategy0ReadOnlyLogging : BaseTestStrategy
    {
        private readonly Dictionary<string, int> tagReadCounts = new();

        public JobStrategy0ReadOnlyLogging(string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings)
            : base(hostname, logFile, readerSettings)
        {
            TagOpController.Instance.CleanUp();
        }

        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;

                Console.WriteLine("Starting Read Logging Test Strategy...");
                Console.WriteLine("Press 'q' to stop the test and return to menu.");

                ConfigureReader();

                reader.TagsReported += OnTagsReported;
                reader.Start();

                if (!File.Exists(logFile))
                    LogToCsv("Timestamp,TID,EPC,ReadCount,RSSI,AntennaPort");

                while (!IsCancellationRequested())
                    Thread.Sleep(100);

                Console.WriteLine("\nStopping test...");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error during reading test: " + ex.Message);
            }
            finally
            {
                CleanupReader();
            }
        }

        private void OnTagsReported(ImpinjReader sender, TagReport? report)
        {
            if (report == null || IsCancellationRequested()) return;

            foreach (Tag tag in report.Tags)
            {
                string tidHex = tag.Tid?.ToHexString() ?? "N/A";
                string epcHex = tag.Epc.ToHexString();

                if (!tagReadCounts.ContainsKey(tidHex))
                    tagReadCounts[tidHex] = 0;

                tagReadCounts[tidHex]++;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                double rssi = tag.PeakRssiInDbm;
                ushort antennaPort = tag.AntennaPortNumber;

                Console.WriteLine($"Read tag TID={tidHex}, EPC={epcHex}, Count={tagReadCounts[tidHex]}");

                LogToCsv($"{timestamp},{tidHex},{epcHex},{tagReadCounts[tidHex]},{rssi},{antennaPort}");
            }
        }

        private void LogToCsv(string data)
        {
            lock (this)
            {
                File.AppendAllText(logFile, data + Environment.NewLine);
            }
        }

        
    }
}



