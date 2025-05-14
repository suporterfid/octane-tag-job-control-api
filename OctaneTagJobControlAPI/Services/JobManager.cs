// OctaneTagJobControlAPI/Services/JobManager.cs
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Repositories;
using OctaneTagWritingTest;
using OctaneTagJobControlAPI.Strategies;
using OctaneTagWritingTest.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OctaneTagJobControlAPI.Strategies;
using OctaneTagJobControlAPI.Extensions;
using OctaneTagJobControlAPI.Strategies.Base;

namespace OctaneTagJobControlAPI.Services
{
    /// <summary>
    /// Manages job execution and status tracking
    /// </summary>
    public class JobManager
    {
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();
        private readonly ConcurrentDictionary<string, Task> _jobTasks = new();
        private readonly ConcurrentDictionary<string, IJobStrategy> _jobStrategies = new();
        private readonly ILogger<JobManager> _logger;
        private readonly IJobRepository _jobRepository;
        private readonly IConfigurationRepository _configRepository;

        public JobManager(
            ILogger<JobManager> logger,
            IJobRepository jobRepository,
            IConfigurationRepository configRepository)
        {
            _logger = logger;
            _jobRepository = jobRepository;
            _configRepository = configRepository;
        }

        /// <summary>
        /// Get all available job strategies that can be executed
        /// </summary>
        public Dictionary<string, string> GetAvailableStrategies()
        {
            var strategies = new Dictionary<string, string>();

            // Find all job strategy classes in the assembly
            var jobTypes = Assembly.GetAssembly(typeof(IJobStrategy))
                ?.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IJobStrategy).IsAssignableFrom(t));

            if (jobTypes != null)
            {
                foreach (var jobType in jobTypes)
                {
                    strategies[jobType.Name] = jobType.FullName;
                }
            }

            return strategies;
        }

        /// <summary>
        /// Register a new job using the given configuration
        /// </summary>
        public async Task<string> RegisterJobAsync(JobConfiguration config)
        {
            var jobId = await _jobRepository.CreateJobAsync(config);

            _logger.LogInformation("Registered new job: {JobId}, {JobName}, Strategy: {Strategy}",
                jobId, config.Name, config.StrategyType);

            return jobId;
        }

        /// <summary>
        /// Start a job with the given ID
        /// </summary>
        public async Task<bool> StartJobAsync(string jobId, int timeoutSeconds = 300)
        {
            // Get the job status and configuration
            var status = await _jobRepository.GetJobStatusAsync(jobId);
            if (status == null)
            {
                _logger.LogError("Cannot start job {JobId}: Job not found", jobId);
                return false;
            }

            var config = await _configRepository.GetConfigurationAsync(status.ConfigurationId);
            if (config == null)
            {
                status.State = JobState.Failed;
                status.ErrorMessage = $"Failed to find configuration with ID {status.ConfigurationId}";
                await _jobRepository.UpdateJobStatusAsync(jobId, status);
                _logger.LogError("Cannot start job {JobId}: Configuration not found", jobId);
                return false;
            }

            if (_jobTasks.ContainsKey(jobId) && !_jobTasks[jobId].IsCompleted)
            {
                _logger.LogWarning("Cannot start job {JobId}: Job is already running", jobId);
                return false;
            }

            try
            {
                // Update status
                status.State = JobState.Running;
                status.StartTime = DateTime.UtcNow;
                status.EndTime = null;
                status.CurrentOperation = "Starting";
                status.ProgressPercentage = 0;
                status.TotalTagsProcessed = 0;
                status.SuccessCount = 0;
                status.FailureCount = 0;
                status.ErrorMessage = string.Empty;

                await _jobRepository.UpdateJobStatusAsync(jobId, status);

                // Create cancellation token
                var cts = new CancellationTokenSource();
                _cancellationTokens[jobId] = cts;

                // Create timeout token
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                // Create and configure strategy
                var strategy = await CreateJobStrategyAsync(config);
                if (strategy == null)
                {
                    status.State = JobState.Failed;
                    status.ErrorMessage = $"Failed to create strategy of type {config.StrategyType}";
                    await _jobRepository.UpdateJobStatusAsync(jobId, status);
                    return false;
                }

                _jobStrategies[jobId] = strategy;

                // Start the job in a background task
                var task = Task.Run(async () =>
                {
                    try
                    {
                        // Log job start
                        await _jobRepository.AddJobLogEntryAsync(jobId, $"Starting job '{status.JobName}' using strategy '{config.StrategyType}'");

                        // Hook up progress monitoring
                        TagOpController.Instance.CleanUp();

                        // Start the strategy
                        strategy.RunJob(cts.Token);

                        // Update completion status
                        if (!cts.Token.IsCancellationRequested)
                        {
                            status.State = JobState.Completed;
                            status.EndTime = DateTime.UtcNow;
                            status.CurrentOperation = "Completed";
                            status.ProgressPercentage = 100;

                            await _jobRepository.UpdateJobStatusAsync(jobId, status);
                            await _jobRepository.AddJobLogEntryAsync(jobId, "Job completed successfully");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        status.State = JobState.Canceled;
                        status.EndTime = DateTime.UtcNow;
                        status.CurrentOperation = "Canceled";

                        await _jobRepository.UpdateJobStatusAsync(jobId, status);
                        await _jobRepository.AddJobLogEntryAsync(jobId, "Job was canceled");
                    }
                    catch (Exception ex)
                    {
                        status.State = JobState.Failed;
                        status.EndTime = DateTime.UtcNow;
                        status.CurrentOperation = "Failed";
                        status.ErrorMessage = ex.Message;

                        await _jobRepository.UpdateJobStatusAsync(jobId, status);
                        await _jobRepository.AddJobLogEntryAsync(jobId, $"Job failed with error: {ex.Message}");

                        _logger.LogError(ex, "Error executing job {JobId}", jobId);
                    }
                }, cts.Token);

                _jobTasks[jobId] = task;

                // Start a timer to update status periodically
                _ = StartStatusUpdateTimerAsync(jobId, cts.Token);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start job {JobId}", jobId);

                status.State = JobState.Failed;
                status.ErrorMessage = ex.Message;
                await _jobRepository.UpdateJobStatusAsync(jobId, status);
                await _jobRepository.AddJobLogEntryAsync(jobId, $"Failed to start job: {ex.Message}");

                return false;
            }
        }

        /// <summary>
        /// Stop a running job
        /// </summary>
        public async Task<bool> StopJobAsync(string jobId)
        {
            if (!_cancellationTokens.TryGetValue(jobId, out var cts))
            {
                return false;
            }

            try
            {
                cts.Cancel();

                var status = await _jobRepository.GetJobStatusAsync(jobId);
                if (status != null)
                {
                    status.State = JobState.Canceled;
                    status.EndTime = DateTime.UtcNow;
                    status.CurrentOperation = "Canceled by user";

                    await _jobRepository.UpdateJobStatusAsync(jobId, status);
                    await _jobRepository.AddJobLogEntryAsync(jobId, "Job was canceled by user");
                }

                _logger.LogInformation("Job {JobId} canceled by user", jobId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping job {JobId}", jobId);
                return false;
            }
        }

        /// <summary>
        /// Get the current status of a job
        /// </summary>
        public async Task<JobStatus> GetJobStatusAsync(string jobId)
        {
            return await _jobRepository.GetJobStatusAsync(jobId);
        }

        /// <summary>
        /// Get the status of all registered jobs
        /// </summary>
        public async Task<List<JobStatus>> GetAllJobStatusesAsync()
        {
            return await _jobRepository.GetAllJobStatusesAsync();
        }

        /// <summary>
        /// Get the configuration of a job
        /// </summary>
        public async Task<JobConfiguration> GetJobConfigurationAsync(string jobId)
        {
            var status = await _jobRepository.GetJobStatusAsync(jobId);
            if (status == null || string.IsNullOrEmpty(status.ConfigurationId))
            {
                return null;
            }

            return await _configRepository.GetConfigurationAsync(status.ConfigurationId);
        }

        /// <summary>
        /// Get log entries for a job
        /// </summary>
        public async Task<List<string>> GetJobLogEntriesAsync(string jobId, int maxEntries = 100)
        {
            return await _jobRepository.GetJobLogEntriesAsync(jobId, maxEntries);
        }

        /// <summary>
        /// Get metrics for a job
        /// </summary>
        public async Task<Dictionary<string, object>> GetJobMetricsAsync(string jobId)
        {
            return await _jobRepository.GetJobMetricsAsync(jobId);
        }

        /// <summary>
        /// Clean up completed or failed jobs
        /// </summary>
        public void CleanupJobs()
        {
            foreach (var jobId in _jobTasks.Keys.ToList())
            {
                // Check if the task has completed
                if (_jobTasks.TryGetValue(jobId, out var task) && task.IsCompleted)
                {
                    // Remove task and CTS
                    _jobTasks.TryRemove(jobId, out _);

                    if (_cancellationTokens.TryRemove(jobId, out var cts))
                    {
                        cts.Dispose();
                    }

                    if (_jobStrategies.TryRemove(jobId, out var strategy))
                    {
                        // Clean up strategy resources if applicable
                        (strategy as IDisposable)?.Dispose();
                    }
                }
            }
        }

        #region Private Methods

        // Excerpt from JobManager.cs - CreateJobStrategyAsync method update
        // The main changes needed are in the CreateJobStrategyAsync method of the JobManager class
        // This method is responsible for creating strategy instances with the appropriate parameters

        // In OctaneTagJobControlAPI/Services/JobManager.cs
        // Look for the CreateJobStrategyAsync method (around line 265)
        // Update the method to extract lock/permalock parameters and pass them to CheckBoxStrategy

        private async Task<IJobStrategy> CreateJobStrategyAsync(JobConfiguration config)
        {
            try
            {
                // Find strategy type
                var strategyType = GetAvailableStrategies()
                    .Where(s => s.Key == config.StrategyType)
                    .Select(s => Type.GetType(s.Value))
                    .FirstOrDefault();

                if (strategyType == null)
                {
                    _logger.LogError("Strategy type {StrategyType} not found", config.StrategyType);
                    return null;
                }

                // Convert the reader settings to the format expected by the strategy
                var readerSettings = ConvertReaderSettings(config.ReaderSettings);

                // Extract common parameters
                string logFilePath = config.LogFilePath;

                // Extract additional parameters that might be needed by specific strategies
                config.Parameters.TryGetValue("epcHeader", out var epcHeader);
                config.Parameters.TryGetValue("sku", out var sku);
                config.Parameters.TryGetValue("encodingMethod", out var encodingMethod);

                // Parse SGTIN-96 specific parameters
                int partitionValue = 6; // Default value
                if (config.Parameters.TryGetValue("partitionValue", out var partitionStr) &&
                    int.TryParse(partitionStr, out int parsedPartition))
                {
                    partitionValue = parsedPartition;
                }

                int itemReference = 0; // Default value
                if (config.Parameters.TryGetValue("itemReference", out var itemRefStr) &&
                    int.TryParse(itemRefStr, out int parsedItemRef))
                {
                    itemReference = parsedItemRef;
                }

                // Extract lock/permalock parameters
                bool enableLock = false;
                if (config.Parameters.TryGetValue("enableLock", out var lockStr))
                {
                    enableLock = bool.TryParse(lockStr, out bool parsedLock) ? parsedLock :
                                 lockStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                bool enablePermalock = false;
                if (config.Parameters.TryGetValue("enablePermalock", out var permalockStr))
                {
                    enablePermalock = bool.TryParse(permalockStr, out bool parsedPermalock) ? parsedPermalock :
                                     permalockStr.Equals("true", StringComparison.OrdinalIgnoreCase);
                }

                // Log the lock settings
                _logger.LogInformation("Lock settings for strategy {StrategyType}: Lock={EnableLock}, Permalock={EnablePermalock}",
                    config.StrategyType, enableLock, enablePermalock);

                // Create the strategy instance based on the type
                if (strategyType == typeof(MultiReaderEnduranceStrategy) ||
                    strategyType.Name == "JobStrategy8MultipleReaderEnduranceStrategy")
                                {
                                    // This strategy uses three different readers
                                    string detectorHostname = config.ReaderSettings.Detector.Hostname;
                                    string writerHostname = config.ReaderSettings.Writer.Hostname;
                                    string verifierHostname = config.ReaderSettings.Verifier.Hostname;

                                    // Check if the strategy class has constructor parameters for lock/permalock
                                    var constructor = strategyType.GetConstructor(new Type[] {
                        typeof(string), typeof(string), typeof(string),
                        typeof(string), typeof(Dictionary<string, ReaderSettings>),
                        typeof(bool), typeof(bool) // Lock parameters
                    });

                    if (constructor != null)
                    {
                        // If the constructor accepts lock parameters, use them
                        return (IJobStrategy)Activator.CreateInstance(
                            strategyType,
                            detectorHostname,
                            writerHostname,
                            verifierHostname,
                            logFilePath,
                            readerSettings,
                            enableLock,
                            enablePermalock);
                    }
                    else
                    {
                        // Use the standard constructor without lock parameters
                        return (IJobStrategy)Activator.CreateInstance(
                            strategyType,
                            detectorHostname,
                            writerHostname,
                            verifierHostname,
                            logFilePath,
                            readerSettings);
                    }
                }
                else if (strategyType == typeof(CheckBoxStrategy) ||
                         strategyType.Name == "CheckBoxStrategy")
                {
                    // This strategy only uses one reader (the writer hostname) but now needs lock parameters
                    string hostname = config.ReaderSettings.Writer.Hostname;

                    return (IJobStrategy)Activator.CreateInstance(
                        strategyType,
                        hostname,
                        logFilePath,
                        readerSettings,
                        epcHeader,
                        sku,
                        encodingMethod,
                        partitionValue,
                        itemReference,
                        enableLock,         // Pass the enableLock parameter
                        enablePermalock);   // Pass the enablePermalock parameter
                }
                else
                {
                    // Default constructor pattern for most strategies
                    string hostname = config.ReaderSettings.Writer.Hostname;
                    return (IJobStrategy)Activator.CreateInstance(
                        strategyType,
                        hostname,
                        logFilePath,
                        readerSettings);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating strategy instance for {StrategyType}", config.StrategyType);
                return null;
            }
        }

        /// <summary>
        /// Convert our API ReaderSettings to the Dictionary format expected by the strategies
        /// </summary>
        private Dictionary<string, OctaneTagWritingTest.RfidDeviceSettings> ConvertReaderSettings(
     ReaderSettingsGroup settingsGroup)
        {
            var result = new Dictionary<string, OctaneTagWritingTest.RfidDeviceSettings>();

            // Convert Detector settings
            if (settingsGroup.Detector != null)
            {
                result["detector"] = settingsGroup.Detector.ToLegacySettings("detector");
            }

            // Convert Writer settings
            if (settingsGroup.Writer != null)
            {
                result["writer"] = settingsGroup.Writer.ToLegacySettings("writer");
            }

            // Convert Verifier settings
            if (settingsGroup.Verifier != null)
            {
                result["verifier"] = settingsGroup.Verifier.ToLegacySettings("verifier");
            }

            return result;
        }

        /// <summary>
        /// Start a timer to periodically update job status
        /// </summary>
        private async Task StartStatusUpdateTimerAsync(string jobId, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Get current job status
                    var status = await _jobRepository.GetJobStatusAsync(jobId);
                    if (status == null || status.State != JobState.Running)
                        break;

                    // Update metrics from TagOpController
                    status.TotalTagsProcessed = TagOpController.Instance.GetTotalReadCount();
                    status.SuccessCount = TagOpController.Instance.GetSuccessCount();
                    status.FailureCount = status.TotalTagsProcessed - status.SuccessCount;

                    // Calculate progress percentage if possible
                    if (status.TotalTagsProcessed > 0)
                    {
                        // Calculate progress based on processed vs success
                        double processed = status.TotalTagsProcessed;
                        double success = status.SuccessCount;

                        status.ProgressPercentage = Math.Min(100, (success / processed) * 100);
                    }

                    // Update metrics
                    var metrics = await _jobRepository.GetJobMetricsAsync(jobId) ?? new Dictionary<string, object>();

                    metrics["memoryUsageMB"] = Math.Round(
                        Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0, 2);
                    metrics["totalTagsProcessed"] = status.TotalTagsProcessed;
                    metrics["successCount"] = status.SuccessCount;
                    metrics["failureCount"] = status.FailureCount;
                    metrics["elapsedSeconds"] = (DateTime.UtcNow - status.StartTime.Value).TotalSeconds;
                    metrics["lastUpdated"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                    // Calculate throughput
                    if (status.StartTime.HasValue && status.TotalTagsProcessed > 0)
                    {
                        double elapsedSeconds = (DateTime.UtcNow - status.StartTime.Value).TotalSeconds;
                        if (elapsedSeconds > 0)
                        {
                            metrics["tagsPerSecond"] = Math.Round(status.TotalTagsProcessed / elapsedSeconds, 2);
                            metrics["successPerSecond"] = Math.Round(status.SuccessCount / elapsedSeconds, 2);
                        }
                    }

                    // Save updated metrics
                    await _jobRepository.UpdateJobMetricsAsync(jobId, metrics);

                    // Save updated status
                    await _jobRepository.UpdateJobStatusAsync(jobId, status);

                    // Wait for next update (1 second)
                    await Task.Delay(1000, token);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in status update timer for job {JobId}", jobId);
                await _jobRepository.AddJobLogEntryAsync(jobId, $"Error updating status: {ex.Message}");
            }
        }
        #endregion
    }
}