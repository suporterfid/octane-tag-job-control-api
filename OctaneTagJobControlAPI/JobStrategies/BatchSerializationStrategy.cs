using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Impinj.OctaneSdk;
using OctaneTagJobControlAPI.JobStrategies.Base;
using OctaneTagJobControlAPI.JobStrategies.Base.Configuration;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagWritingTest.Helpers;

namespace OctaneTagJobControlAPI.JobStrategies
{
    /// <summary>
    /// Strategy for batch serializing and optionally permalocking tags efficiently
    /// </summary>
    [StrategyDescription(
        "Processes tags in a batch for efficient serialization and permalocking",
        "Writing",
        StrategyCapability.Reading | StrategyCapability.Writing | StrategyCapability.BatchProcessing | StrategyCapability.Permalock)]
    public class BatchSerializationStrategy : SingleReaderStrategyBase
    {
        // Collection to queue tags for batch processing
        private readonly ConcurrentQueue<Tag> _tagsQueue = new ConcurrentQueue<Tag>();

        // Timers for performance tracking
        private readonly ConcurrentDictionary<string, Stopwatch> _writeTimers = new ConcurrentDictionary<string, Stopwatch>();

        // Completion results storage
        private readonly ConcurrentDictionary<string, bool> _completedTags = new ConcurrentDictionary<string, bool>();

        // Settings for this strategy
        private bool _enablePermalock = false;
        private int _batchInterval = 100; // milliseconds between processing batches
        private int _maxBatchSize = 50;   // maximum tags to process in a batch

        // Status tracking
        private readonly Stopwatch _runTimer = new Stopwatch();
        private JobExecutionStatus _status = new JobExecutionStatus();

        // Background processing task
        private Task _batchProcessingTask;
        private CancellationTokenSource _processingCts;

        /// <summary>
        /// Initializes a new instance of the BatchSerializationStrategy class
        /// </summary>
        /// <param name="hostname">The hostname of the RFID reader</param>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="readerSettings">Dictionary of reader settings</param>
        public BatchSerializationStrategy(
            string hostname,
            string logFile,
            Dictionary<string, ReaderSettings> readerSettings)
            : base(hostname, logFile, readerSettings)
        {
            _status.CurrentOperation = "Initialized";
            TagOpController.Instance.CleanUp();
        }

        /// <summary>
        /// Executes the batch serialization job
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        public override void RunJob(CancellationToken cancellationToken = default)
        {
            try
            {
                this.cancellationToken = cancellationToken;
                _status.CurrentOperation = "Starting";
                _runTimer.Start();

                Console.WriteLine("=== Batch Serialization Strategy ===");
                Console.WriteLine($"Permalock: {_enablePermalock}");
                Console.WriteLine("Press 'q' to stop the test and return to menu.");

                // Load any required EPC list
                EpcListManager.Instance.LoadEpcList("epc_list.txt");

                // Extract permalock setting from parameters if available
                var writerSettings = GetSettingsForRole("writer");

                // Get configuration parameters from base class if available
                // Note: In a real implementation, we would need to access these parameters 
                // through the appropriate configuration object - this is a placeholder
                // until we know the actual way parameters are passed in the project

                // Try to get the permalock setting - this should be adapted to match how parameters are passed
                try
                {
                    // Example of how we might check for a permalock parameter:
                    // 1. Check if the writer settings has a property or field for permalocking
                    // 2. Or access parameters from configuration if available

                    // Placeholder implementation - in a real scenario this would access
                    // the actual configuration parameter from WriteStrategyConfiguration
                    _enablePermalock = false; // Default value

                    // For example, if running under the job manager with a WriteStrategyConfiguration:
                    // if (configuration is WriteStrategyConfiguration writeConfig)
                    // {
                    //    _enablePermalock = writeConfig.PermalockAfterWrite;
                    // }

                    Console.WriteLine($"Permalock setting: {_enablePermalock}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error accessing permalock configuration: {ex.Message}");
                    // Default to false if there's any error
                    _enablePermalock = false;
                }

                // Configure the reader
                ConfigureReader();

                // Attach event handlers
                reader.TagsReported += OnTagsReported;
                reader.TagOpComplete += OnTagOpComplete;

                // Start the reader
                reader.Start();

                // Initialize batch processing
                _processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _batchProcessingTask = Task.Run(() => ProcessTagsBatchAsync(_processingCts.Token), _processingCts.Token);

                // Create CSV header if the log file does not exist
                if (!File.Exists(logFile))
                {
                    LogToCsv("Timestamp,TID,OldEPC,NewEPC,SerialCounter,WriteTime,Result,RSSI,AntennaPort,ChipModel");
                }

                _status.CurrentOperation = "Processing";

                // Main loop - wait for cancellation
                while (!cancellationToken.IsCancellationRequested)
                {
                    // Check for keyboard input to exit
                    if (Console.KeyAvailable && Console.ReadKey(true).KeyChar == 'q')
                    {
                        break;
                    }

                    // Update status every 100ms
                    Thread.Sleep(100);
                    _status.RunTime = _runTimer.Elapsed;
                }

                _status.CurrentOperation = "Stopping";
                Console.WriteLine("\nStopping test...");
            }
            catch (Exception ex)
            {
                _status.CurrentOperation = "Error";
                Console.WriteLine("Error in batch serialization strategy: " + ex.Message);
            }
            finally
            {
                _runTimer.Stop();

                // Stop background processing
                _processingCts?.Cancel();
                try
                {
                    _batchProcessingTask?.Wait(1000);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }

                // Cleanup resources
                CleanupReader();
            }
        }

        /// <summary>
        /// Background task to process tags in batches
        /// </summary>
        private async Task ProcessTagsBatchAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Process up to maxBatchSize tags in each iteration
                    int processedCount = 0;
                    while (processedCount < _maxBatchSize && _tagsQueue.TryDequeue(out var tag))
                    {
                        if (token.IsCancellationRequested)
                            break;

                        await ProcessTagAsync(tag, token);
                        processedCount++;
                    }

                    // Update status with current counts
                    lock (_status)
                    {
                        _status.TotalTagsProcessed = _completedTags.Count + _tagsQueue.Count;
                        _status.SuccessCount = _completedTags.Count(kv => kv.Value);
                        _status.FailureCount = _completedTags.Count(kv => !kv.Value);
                        _status.ProgressPercentage = _status.TotalTagsProcessed > 0
                            ? (double)_status.SuccessCount / _status.TotalTagsProcessed * 100
                            : 0;
                    }

                    // Wait before next batch
                    await Task.Delay(_batchInterval, token);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in batch processing: {ex.Message}");
                    // Continue processing despite errors
                }
            }
        }

        /// <summary>
        /// Process a single tag
        /// </summary>
        private async Task ProcessTagAsync(Tag tag, CancellationToken token)
        {
            var tidHex = tag.Tid.ToHexString();
            var epcHex = tag.Epc?.ToHexString() ?? string.Empty;

            // Get or generate a new EPC for this tag
            var newEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
            if (string.IsNullOrEmpty(newEpc))
            {
                newEpc = TagOpController.Instance.GetNextEpcForTag(epcHex, tidHex);
                TagOpController.Instance.RecordExpectedEpc(tidHex, newEpc);
            }

            Console.WriteLine($"Batch writing EPC {newEpc} to tag {tidHex}");

            // Start timing the write operation
            var swWrite = _writeTimers.GetOrAdd(tidHex, _ => new Stopwatch());
            swWrite.Reset();
            swWrite.Start();

            // Trigger the write operation
            try
            {
                if (_enablePermalock)
                {
                    // For permalock operations, we need to first write then permalock
                    TagOpController.Instance.TriggerWriteAndVerify(
                        tag,
                        newEpc,
                        reader,
                        token,
                        swWrite,
                        newAccessPassword,
                        true);

                    // Small delay to ensure write completes before permalock
                    await Task.Delay(50, token);

                    // Add permalock operation
                    if (!token.IsCancellationRequested)
                    {
                        TagOpController.Instance.PermaLockTag(tag, newAccessPassword, reader);
                    }
                }
                else
                {
                    // Standard write operation
                    TagOpController.Instance.TriggerWriteAndVerify(
                        tag,
                        newEpc,
                        reader,
                        token,
                        swWrite,
                        newAccessPassword,
                        true);
                }
            }
            catch (Exception ex)
            {
                swWrite.Stop();
                Console.WriteLine($"Error processing tag {tidHex}: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles tag report events from the reader
        /// </summary>
        private void OnTagsReported(ImpinjReader sender, TagReport report)
        {
            if (report == null || cancellationToken.IsCancellationRequested)
                return;

            foreach (var tag in report.Tags)
            {
                var tidHex = tag.Tid?.ToHexString() ?? string.Empty;
                if (string.IsNullOrEmpty(tidHex))
                    continue;

                // Skip processed tags
                if (TagOpController.Instance.HasResult(tidHex) ||
                    TagOpController.Instance.IsTidProcessed(tidHex) ||
                    _completedTags.ContainsKey(tidHex))
                    continue;

                // Queue for processing
                _tagsQueue.Enqueue(tag);
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
                var tidHex = result.Tag.Tid?.ToHexString() ?? "N/A";

                // Handle write operation results
                if (result is TagWriteOpResult writeResult)
                {
                    ProcessWriteResult(writeResult, tidHex);
                }
                // Handle lock operation results
                else if (result is TagLockOpResult lockResult)
                {
                    ProcessLockResult(lockResult, tidHex);
                }
            }
        }

        /// <summary>
        /// Process a write operation result
        /// </summary>
        private void ProcessWriteResult(TagWriteOpResult writeResult, string tidHex)
        {
            // Stop the timer for this TID
            if (_writeTimers.TryGetValue(tidHex, out var timer))
            {
                timer.Stop();
            }

            // Get result details
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var oldEpc = writeResult.Tag.Epc.ToHexString();
            var newEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
            var resultStatus = writeResult.Result.ToString();
            var success = resultStatus == "Success";
            var writeTime = timer?.ElapsedMilliseconds ?? 0;
            var resultRssi = writeResult.Tag.IsPeakRssiInDbmPresent ? writeResult.Tag.PeakRssiInDbm : 0;
            var antennaPort = writeResult.Tag.IsAntennaPortNumberPresent ? writeResult.Tag.AntennaPortNumber : (ushort)0;
            var chipModel = TagOpController.Instance.GetChipModel(writeResult.Tag);

            // If not permalocking, mark as complete
            if (!_enablePermalock)
            {
                // Log the result
                LogToCsv($"{timestamp},{tidHex},{oldEpc},{newEpc},{TagOpController.Instance.GetSuccessCount()},{writeTime},{resultStatus},{resultRssi},{antennaPort},{chipModel}");

                // Record the result
                TagOpController.Instance.RecordResult(tidHex, resultStatus, success);
                _completedTags.TryAdd(tidHex, success);

                Console.WriteLine($"Write complete for TID={tidHex}: Result={resultStatus}, Time={writeTime}ms");
            }
            else if (!success)
            {
                // If permalocking but write failed, mark as complete with failure
                LogToCsv($"{timestamp},{tidHex},{oldEpc},{newEpc},{TagOpController.Instance.GetSuccessCount()},{writeTime},{resultStatus},{resultRssi},{antennaPort},{chipModel}");
                TagOpController.Instance.RecordResult(tidHex, resultStatus, false);
                _completedTags.TryAdd(tidHex, false);

                Console.WriteLine($"Write failed for TID={tidHex} before permalock: Result={resultStatus}, Time={writeTime}ms");
            }
        }

        /// <summary>
        /// Process a lock operation result
        /// </summary>
        private void ProcessLockResult(TagLockOpResult lockResult, string tidHex)
        {
            // Get result details
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var epc = lockResult.Tag.Epc.ToHexString();
            var newEpc = TagOpController.Instance.GetExpectedEpc(tidHex);
            var resultStatus = lockResult.Result.ToString();
            var success = resultStatus == "Success";
            var writeTime = _writeTimers.TryGetValue(tidHex, out var timer) ? timer.ElapsedMilliseconds : 0;
            var resultRssi = lockResult.Tag.IsPeakRssiInDbmPresent ? lockResult.Tag.PeakRssiInDbm : 0;
            var antennaPort = lockResult.Tag.IsAntennaPortNumberPresent ? lockResult.Tag.AntennaPortNumber : (ushort)0;
            var chipModel = TagOpController.Instance.GetChipModel(lockResult.Tag);

            // Log the result with "Permalocked" or "PermalockFailed" status
            var finalStatus = success ? "Permalocked" : "PermalockFailed";
            LogToCsv($"{timestamp},{tidHex},{epc},{newEpc},{TagOpController.Instance.GetSuccessCount()},{writeTime},{finalStatus},{resultRssi},{antennaPort},{chipModel}");

            // Record the result
            TagOpController.Instance.RecordResult(tidHex, finalStatus, success);
            _completedTags.TryAdd(tidHex, success);

            Console.WriteLine($"Permalock operation for TID={tidHex}: Result={finalStatus}, Time={writeTime}ms");
        }

        /// <summary>
        /// Gets the current status of the job execution
        /// </summary>
        public override JobExecutionStatus GetStatus()
        {
            lock (_status)
            {
                // Calculate stats for metrics
                double tagsPerSecond = _status.RunTime.TotalSeconds > 0
                    ? _status.SuccessCount / _status.RunTime.TotalSeconds
                    : 0;
                double avgWriteTime = _writeTimers.Count > 0
                    ? _writeTimers.Values.Average(sw => sw.ElapsedMilliseconds)
                    : 0;

                return new JobExecutionStatus
                {
                    TotalTagsProcessed = _status.TotalTagsProcessed,
                    SuccessCount = _status.SuccessCount,
                    FailureCount = _status.FailureCount,
                    ProgressPercentage = _status.ProgressPercentage,
                    CurrentOperation = _status.CurrentOperation,
                    RunTime = _status.RunTime,
                    Metrics = new Dictionary<string, object>
                    {
                        { "BatchSize", _maxBatchSize },
                        { "BatchInterval", _batchInterval },
                        { "PermalockEnabled", _enablePermalock },
                        { "QueuedTags", _tagsQueue.Count },
                        { "ProcessedTags", _completedTags.Count },
                        { "TagsPerSecond", Math.Round(tagsPerSecond, 2) },
                        { "AverageWriteTimeMs", Math.Round(avgWriteTime, 2) },
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
                Name = "BatchSerializationStrategy",
                Description = "Processes tags in a batch for efficient serialization and permalocking",
                Category = "Writing",
                ConfigurationType = typeof(WriteStrategyConfiguration),
                Capabilities = StrategyCapability.Reading | StrategyCapability.Writing |
                              StrategyCapability.BatchProcessing | StrategyCapability.Permalock,
                RequiresMultipleReaders = false
            };
        }
    }
}