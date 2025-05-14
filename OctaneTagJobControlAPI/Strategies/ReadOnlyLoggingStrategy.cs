using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagJobControlAPI.Strategies.Base.Configuration;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagWritingTest.Helpers;
using OctaneTagJobControlAPI.Models;

namespace OctaneTagJobControlAPI.Strategies
{
    /// <summary>
    /// Read-only logging strategy that reads tags and logs their information without modifying them
    /// </summary>
    [StrategyDescription(
        "Logs tag information without modifying tags",
        "Basic Reading",
        StrategyCapability.Reading)]
    public class ReadOnlyLoggingStrategy : SingleReaderStrategyBase
    {
        private readonly Dictionary<string, int> _tagReadCounts = new();
        private readonly Stopwatch _runTimer = new();
        private JobExecutionStatus _status = new();

        /// <summary>
        /// Initializes a new instance of the ReadOnlyLoggingStrategy class
        /// </summary>
        /// <param name="hostname">The hostname of the RFID reader</param>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="readerSettings">Dictionary of reader settings</param>
        public ReadOnlyLoggingStrategy(
            string hostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings,
            IServiceProvider serviceProvider = null)
            : base(hostname, logFile, readerSettings, serviceProvider)
        {
            _status.CurrentOperation = "Initialized";
            TagOpController.Instance.CleanUp();
        }

        /// <summary>
        /// Executes the read-only logging job
        /// </summary>
        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                _status.CurrentOperation = "Starting";

                Console.WriteLine("Starting Read Logging Strategy...");

                ConfigureReader();

                reader.TagsReported += OnTagsReported;
                reader.Start();
                _runTimer.Start();

                if (!File.Exists(logFile))
                    LogToCsv("Timestamp,TID,EPC,ReadCount,RSSI,AntennaPort");

                _status.CurrentOperation = "Reading Tags";

                while (!cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(100);
                    _status.RunTime = _runTimer.Elapsed;
                }

                _status.CurrentOperation = "Stopping";
                Console.WriteLine("\nStopping test...");
            }
            catch (Exception ex)
            {
                _status.CurrentOperation = "Error";
                Console.WriteLine("Error during reading test: " + ex.Message);
            }
            finally
            {
                _runTimer.Stop();
                CleanupReader();
            }
        }

        /// <summary>
        /// Handles tag report events from the reader
        /// </summary>
        private void OnTagsReported(ImpinjReader sender, TagReport report)
        {
            if (report == null || cancellationToken.IsCancellationRequested) return;

            foreach (Tag tag in report.Tags)
            {
                string tidHex = tag.Tid?.ToHexString() ?? "N/A";
                string epcHex = tag.Epc.ToHexString();

                if (!_tagReadCounts.ContainsKey(tidHex))
                    _tagReadCounts[tidHex] = 0;

                _tagReadCounts[tidHex]++;

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                double rssi = tag.PeakRssiInDbm;
                ushort antennaPort = tag.AntennaPortNumber;

                Console.WriteLine($"Read tag TID={tidHex}, EPC={epcHex}, Count={_tagReadCounts[tidHex]}");
                LogToCsv($"{timestamp},{tidHex},{epcHex},{_tagReadCounts[tidHex]},{rssi},{antennaPort}");

                ReportTagData(new TagReadData
                {
                        TID = tidHex,
                        EPC = epcHex,
                        RSSI = tag.PeakRssiInDbm,
                        AntennaPort = antennaPort,
                        Timestamp = DateTime.Now,
                        ReadCount = 1,
                        PcBits = tag.PcBits.ToString() ?? string.Empty,
                        AdditionalData = new Dictionary<string, object>
                        {
                            { "FastID", tag.IsFastIdPresent },
                            { "Phase", tag.PhaseAngleInRadians },
                            { "ChannelInMhz", tag.ChannelInMhz },
                            { "TagReadCount",_tagReadCounts[tidHex]}
                        }
                });

                lock (_status)
                {
                    _status.TotalTagsProcessed = _tagReadCounts.Count;
                    _status.SuccessCount = _tagReadCounts.Count;
                }
            }
        }

        /// <summary>
        /// Gets the current status of the job execution
        /// </summary>
        public override JobExecutionStatus GetStatus()
        {
            lock (_status)
            {
                return new JobExecutionStatus
                {
                    TotalTagsProcessed = _status.TotalTagsProcessed,
                    SuccessCount = _status.SuccessCount,
                    FailureCount = _status.FailureCount,
                    ProgressPercentage = _status.TotalTagsProcessed > 0 ? 100.0 : 0.0,
                    CurrentOperation = _status.CurrentOperation,
                    RunTime = _status.RunTime,
                    Metrics = new Dictionary<string, object>
                    {
                        { "UniqueTagsRead", _tagReadCounts.Count },
                        { "TotalReads", _tagReadCounts.Values.Sum() },
                        { "ElapsedSeconds", _runTimer.Elapsed.TotalSeconds }
                    }
                };
            }
        }

        /// <summary>
        /// Gets the metadata for this strategy
        /// </summary>
        public override StrategyMetadata GetMetadata()
        {
            return new StrategyMetadata
            {
                Name = "ReadOnlyLoggingStrategy",
                Description = "Logs tag information without modifying tags",
                Category = "Basic Reading",
                ConfigurationType = typeof(ReadOnlyStrategyConfiguration),
                Capabilities = StrategyCapability.Reading,
                RequiresMultipleReaders = false
            };
        }
    }
}