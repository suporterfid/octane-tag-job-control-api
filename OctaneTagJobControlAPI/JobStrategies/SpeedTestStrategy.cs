using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Impinj.OctaneSdk;
using OctaneTagJobControlAPI.JobStrategies.Base.Configuration;
using OctaneTagJobControlAPI.JobStrategies.Base;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagWritingTest.Helpers;
using OctaneTagJobControlAPI.Models;

namespace OctaneTagJobControlAPI.Strategies
{
    /// <summary>
    /// Strategy for measuring the speed of writing new EPCs to tags
    /// </summary>
    [StrategyDescription(
        "Measures the speed of writing new EPCs to tags for optimal performance",
        "Writing",
        StrategyCapability.Reading | StrategyCapability.Writing)]
    public class SpeedTestStrategy : SingleReaderStrategyBase
    {
        private readonly ConcurrentDictionary<string, Stopwatch> _writeTimers = new();
        private readonly Stopwatch _runTimer = new();
        private JobExecutionStatus _status = new();

        /// <summary>
        /// Initializes a new instance of the SpeedTestStrategy class
        /// </summary>
        /// <param name="hostname">The hostname of the RFID reader</param>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="readerSettings">Dictionary of reader settings</param>
        public SpeedTestStrategy(
            string hostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings)
            : base(hostname, logFile, readerSettings)
        {
            _status.CurrentOperation = "Initialized";
            TagOpController.Instance.CleanUp();
        }

        /// <summary>
        /// Executes the speed test job
        /// </summary>
        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                _status.CurrentOperation = "Starting";

                Console.WriteLine("Executing Speed Test Strategy...");
                Console.WriteLine("Press 'q' to stop the test and return to menu.");

                ConfigureReader();

                reader.TagsReported += OnTagsReported;
                reader.TagOpComplete += OnTagOpComplete;
                reader.Start();
                _runTimer.Start();

                if (!File.Exists(logFile))
                {
                    TagOpController.Instance.LogToCsv(logFile, "Timestamp,TID,OldEPC,NewEPC,WriteTime,Result,RSSI,AntennaPort");
                }

                _status.CurrentOperation = "Reading and Writing Tags";

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
                Console.WriteLine("Test error: " + ex.Message);
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
            if (report == null || cancellationToken.IsCancellationRequested)
                return;

            foreach (Tag tag in report.Tags)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                string tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                string epcHex = tag.Epc?.ToHexString() ?? string.Empty;

                if (TagOpController.Instance.IsTidProcessed(tidHex) || TagOpController.Instance.HasResult(tidHex))
                    continue;

                string expectedEpc = TagOpController.Instance.GetExpectedEpc(tidHex);

                if (!string.IsNullOrEmpty(expectedEpc) && expectedEpc.Equals(epcHex, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Verified Tag {tidHex} already has EPC: {epcHex}");
                    TagOpController.Instance.RecordResult(tidHex, epcHex, true);

                    lock (_status)
                    {
                        _status.TotalTagsProcessed++;
                        _status.SuccessCount++;
                    }

                    continue;
                }

                if (string.IsNullOrEmpty(expectedEpc))
                {
                    expectedEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);
                    TagOpController.Instance.RecordExpectedEpc(tidHex, expectedEpc);
                    Console.WriteLine($"Assigning new EPC to TID {tidHex}: {epcHex} -> {expectedEpc}");

                    var writeTimer = new Stopwatch();
                    _writeTimers[tidHex] = writeTimer;

                    TagOpController.Instance.TriggerWriteAndVerify(
                        tag,
                        expectedEpc,
                        reader,
                        cancellationToken,
                        writeTimer,
                        newAccessPassword,
                        true);

                    lock (_status)
                    {
                        _status.TotalTagsProcessed++;
                    }
                }
            }
        }

        /// <summary>
        /// Handles tag operation completion events from the reader
        /// </summary>
        private void OnTagOpComplete(ImpinjReader sender, TagOpReport report)
        {
            if (report == null || cancellationToken.IsCancellationRequested)
                return;

            foreach (TagOpResult result in report)
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                if (result is TagWriteOpResult writeResult)
                {
                    string tidHex = writeResult.Tag.Tid?.ToHexString() ?? "N/A";
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string oldEpc = writeResult.Tag.Epc.ToHexString();
                    string newEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
                    string resultStatus = writeResult.Result.ToString();
                    double resultRssi = writeResult.Tag.IsPeakRssiInDbmPresent ? writeResult.Tag.PeakRssiInDbm : 0;
                    ushort antennaPort = writeResult.Tag.IsAntennaPortNumberPresent ? writeResult.Tag.AntennaPortNumber : (ushort)0;

                    long writeTime = 0;
                    if (_writeTimers.TryGetValue(tidHex, out var timer))
                    {
                        writeTime = timer.ElapsedMilliseconds;
                        timer.Stop();
                    }

                    TagOpController.Instance.LogToCsv(
                        logFile,
                        $"{timestamp},{tidHex},{oldEpc},{newEpc},{writeTime},{resultStatus},{resultRssi},{antennaPort}");

                    bool success = resultStatus == "Success";
                    TagOpController.Instance.RecordResult(tidHex, resultStatus, success);

                    lock (_status)
                    {
                        if (success)
                        {
                            _status.SuccessCount++;
                        }
                        else
                        {
                            _status.FailureCount++;
                        }

                        // Update metrics
                        _status.Metrics["AverageWriteTimeMs"] = _writeTimers.Values
                            .Where(t => t.ElapsedMilliseconds > 0)
                            .Select(t => t.ElapsedMilliseconds)
                            .DefaultIfEmpty(0)
                            .Average();
                    }

                    Console.WriteLine($"Write operation for TID {tidHex}: {resultStatus} in {writeTime}ms");
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
                double writtenPerSecond = _status.RunTime.TotalSeconds > 0
                    ? _status.SuccessCount / _status.RunTime.TotalSeconds
                    : 0;

                return new JobExecutionStatus
                {
                    TotalTagsProcessed = _status.TotalTagsProcessed,
                    SuccessCount = _status.SuccessCount,
                    FailureCount = _status.FailureCount,
                    ProgressPercentage = _status.TotalTagsProcessed > 0
                        ? (double)_status.SuccessCount / _status.TotalTagsProcessed * 100
                        : 0,
                    CurrentOperation = _status.CurrentOperation,
                    RunTime = _status.RunTime,
                    Metrics = new Dictionary<string, object>
                    {
                        { "TagsProcessed", _status.TotalTagsProcessed },
                        { "WrittenTags", _status.SuccessCount },
                        { "FailedWrites", _status.FailureCount },
                        { "TagsPerSecond", writtenPerSecond },
                        { "AverageWriteTimeMs", _status.Metrics.TryGetValue("AverageWriteTimeMs", out var avgTime) ? avgTime : 0 },
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
                Name = "SpeedTestStrategy",
                Description = "Measures the speed of writing new EPCs to tags for optimal performance",
                Category = "Writing",
                ConfigurationType = typeof(WriteStrategyConfiguration),
                Capabilities = StrategyCapability.Reading | StrategyCapability.Writing,
                RequiresMultipleReaders = false
            };
        }
    }
}