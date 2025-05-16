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
using OctaneTagJobControlAPI.Extensions;
using OctaneTagJobControlAPI.Strategies.Base;
using Microsoft.Extensions.DependencyInjection;
using OctaneTagJobControlAPI.Strategies.Base.Configuration;

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
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TagReadData>> _jobTagData = new();
        private readonly ILogger<JobManager> _logger;
        private readonly IJobRepository _jobRepository;
        private readonly IConfigurationRepository _configRepository;
        private readonly IServiceProvider _serviceProvider;
        private StrategyFactory _strategyFactory;

        // Add a locking object for synchronization
        private readonly object _activeJobLock = new object();

        // Track the currently active job ID
        private string _activeJobId = null;

        // Status message for job already running scenario
        private const string JOB_ALREADY_RUNNING_MESSAGE = "Another job is currently running. Only one job can be active at a time.";

        public JobManager(
            ILogger<JobManager> logger,
            IServiceProvider serviceProvider,
            IJobRepository jobRepository,
            IConfigurationRepository configRepository)
        {
            _logger = logger;
            _jobRepository = jobRepository;
            _configRepository = configRepository;
            _serviceProvider = serviceProvider;
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
        /// Checks if there is any job currently running
        /// </summary>
        /// <returns>True if there is an active job; otherwise, false</returns>
        public bool IsAnyJobRunning()
        {
            lock (_activeJobLock)
            {
                return _activeJobId != null;
            }
        }

        /// <summary>
        /// Gets the ID of the active job, if any
        /// </summary>
        /// <returns>The ID of the currently active job, or null if no job is running</returns>
        public string GetActiveJobId()
        {
            lock (_activeJobLock)
            {
                return _activeJobId;
            }
        }

        /// <summary>
        /// Start a job with the given ID
        /// </summary>
        /// <param name="jobId">The ID of the job to start</param>
        /// <param name="timeoutSeconds">Timeout in seconds after which the job will be automatically canceled</param>
        /// <returns>True if the job was started successfully; otherwise, false</returns>
        public async Task<bool> StartJobAsync(string jobId, int timeoutSeconds = 300)
        {
            // Check if another job is already running
            lock (_activeJobLock)
            {
                if (_activeJobId != null)
                {
                    _logger.LogWarning("Cannot start job {JobId}: Another job {ActiveJobId} is already running",
                        jobId, _activeJobId);
                    return false;
                }
            }

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
                var strategy = await CreateJobStrategyAsync(config, jobId);
                if (strategy == null)
                {
                    status.State = JobState.Failed;
                    status.ErrorMessage = $"Failed to create strategy of type {config.StrategyType}";
                    await _jobRepository.UpdateJobStatusAsync(jobId, status);
                    return false;
                }

                (strategy as JobStrategyBase)?.SetJobId(jobId);

                _jobStrategies[jobId] = strategy;

                // Set as the active job (under lock)
                lock (_activeJobLock)
                {
                    _activeJobId = jobId;
                }

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
                    finally
                    {
                        // Clear the active job when it's done (under lock)
                        lock (_activeJobLock)
                        {
                            if (_activeJobId == jobId)
                            {
                                _activeJobId = null;
                            }
                        }
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

                // Clear the active job in case of error (under lock)
                lock (_activeJobLock)
                {
                    if (_activeJobId == jobId)
                    {
                        _activeJobId = null;
                    }
                }

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
                try
                {
                    var status = await _jobRepository.GetJobStatusAsync(jobId);
                    if (status != null)
                    {
                        status.State = JobState.Canceled;
                        status.EndTime = DateTime.UtcNow;
                        status.CurrentOperation = "Canceled by user";

                        await _jobRepository.UpdateJobStatusAsync(jobId, status);
                        await _jobRepository.AddJobLogEntryAsync(jobId, "Job was canceled by user");
                        return true;
                    }
                    else
                    {
                        return false;
                        _logger.LogWarning("Job {JobId} is not running or already stopped", jobId);
                    }
                        
                }
                catch (Exception)
                {
                    return false;
                    _logger.LogWarning("Job {JobId} is not running or already stopped", jobId);

                }
                
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

                // Clear the active job when it's stopped (under lock)
                lock (_activeJobLock)
                {
                    if (_activeJobId == jobId)
                    {
                        _activeJobId = null;
                    }
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
            // Track jobs that need to have their active status cleared
            var jobsToDeactivate = new List<string>();

            foreach (var jobId in _jobTasks.Keys.ToList())
            {
                // Check if the task has completed
                if (_jobTasks.TryGetValue(jobId, out var task) && task.IsCompleted)
                {
                    // Add to the list of jobs to deactivate
                    jobsToDeactivate.Add(jobId);

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

            // Clear active job status for completed jobs (under lock)
            if (jobsToDeactivate.Count > 0)
            {
                lock (_activeJobLock)
                {
                    if (_activeJobId != null && jobsToDeactivate.Contains(_activeJobId))
                    {
                        _activeJobId = null;
                    }
                }
            }

            foreach (var jobId in _jobTagData.Keys.ToList())
            {
                var status = _jobRepository.GetJobStatusAsync(jobId).GetAwaiter().GetResult();
                if (status?.IsFinished == true)
                {
                    _jobTagData.TryRemove(jobId, out _);
                }
            }
        }

        public void ReportTagData(string jobId, TagReadData tagData)
        {
            // Ensure the job dictionary exists
            var jobTags = _jobTagData.GetOrAdd(jobId, _ => new ConcurrentDictionary<string, TagReadData>());

            // Use TID as the key if available, otherwise EPC
            string key = !string.IsNullOrEmpty(tagData.TID) ? tagData.TID : tagData.EPC;

            // Update existing tag data or add new
            jobTags.AddOrUpdate(key, tagData, (_, existing) => {
                existing.ReadCount++;
                existing.Timestamp = tagData.Timestamp;
                existing.RSSI = tagData.RSSI;
                existing.AntennaPort = tagData.AntennaPort;
                // Only update EPC if the new one isn't empty and different
                if (!string.IsNullOrEmpty(tagData.EPC) && existing.EPC != tagData.EPC)
                    existing.EPC = tagData.EPC;
                return existing;
            });
        }

        public TagDataResponse GetJobTagData(string jobId)
        {
            if (!_jobTagData.TryGetValue(jobId, out var jobTags))
            {
                return new TagDataResponse
                {
                    JobId = jobId,
                    Tags = new List<TagReadData>(),
                    TotalCount = 0,
                    UniqueCount = 0,
                    LastUpdated = DateTime.UtcNow
                };
            }

            var tags = jobTags.Values.ToList();
            int totalReads = tags.Sum(t => t.ReadCount);

            return new TagDataResponse
            {
                JobId = jobId,
                Tags = tags,
                TotalCount = totalReads,
                UniqueCount = tags.Count,
                LastUpdated = tags.Any() ? tags.Max(t => t.Timestamp) : DateTime.UtcNow
            };
        }

        public void ClearJobTagData(string jobId)
        {
            _jobTagData.TryRemove(jobId, out _);
        }

        #region Private Methods

        private async Task<IJobStrategy> CreateJobStrategyAsync(JobConfiguration config, string jobId = null)
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

                // Get the strategy factory from the service provider
                if (_strategyFactory == null)
                {
                    _strategyFactory = _serviceProvider.GetRequiredService<StrategyFactory>();
                }

                // Convert JobConfiguration to StrategyConfiguration
                var strategyConfig = ConvertToStrategyConfiguration(config, strategyType);

                // Create the strategy and pass the jobId
                return _strategyFactory.CreateStrategy(config.StrategyType, strategyConfig, jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating strategy instance for {StrategyType}", config.StrategyType);
                return null;
            }
        }

        private StrategyConfiguration ConvertToStrategyConfiguration(JobConfiguration jobConfig, Type strategyType)
        {
            // Determine which type of configuration to create based on the strategy type
            StrategyConfiguration config;

            if (strategyType == typeof(CheckBoxStrategy) ||
                strategyType.Name == "CheckBoxStrategy")
            {
                var encodingConfig = new EncodingStrategyConfiguration();

                // Set encoding specific properties
                if (jobConfig.Parameters.TryGetValue("epcHeader", out var epcHeader))
                    encodingConfig.EpcHeader = epcHeader;

                if (jobConfig.Parameters.TryGetValue("sku", out var sku))
                    encodingConfig.Sku = sku;

                if (jobConfig.Parameters.TryGetValue("encodingMethod", out var encodingMethod))
                    encodingConfig.EncodingMethod = encodingMethod;

                if (jobConfig.Parameters.TryGetValue("partitionValue", out var partitionStr) &&
                    int.TryParse(partitionStr, out int partition))
                    encodingConfig.PartitionValue = partition;

                if (jobConfig.Parameters.TryGetValue("itemReference", out var itemRefStr) &&
                    int.TryParse(itemRefStr, out int itemRef))
                    encodingConfig.ItemReference = itemRef;

                config = encodingConfig;
            }
            else if (typeof(MultiReaderStrategyBase).IsAssignableFrom(strategyType))
            {
                if (strategyType.Name.Contains("MultiAntenna"))
                {
                    var multiAntennaConfig = new MultiAntennaStrategyConfiguration();
                    config = multiAntennaConfig;
                }
                else if (strategyType.Name.Contains("Endurance"))
                {
                    var newEnduranceConfig = new EnduranceTestConfiguration();
                    config = newEnduranceConfig;
                }
                else
                {
                    config = new WriteStrategyConfiguration();
                }
            }
            else if (strategyType.Name.Contains("ReadOnly") ||
                     strategyType.Name.Contains("Reading") ||
                     strategyType == typeof(ReadOnlyLoggingStrategy))
            {
                var readConfig = new ReadOnlyStrategyConfiguration();
                config = readConfig;
            }
            else
            {
                // Default to write strategy configuration
                config = new WriteStrategyConfiguration();
            }

            // Set common properties
            config.LogFilePath = jobConfig.LogFilePath;

            // Convert reader settings
            var readerSettings = new ReaderSettingsGroup
            {
                Detector = jobConfig.ReaderSettingsGroup?.Detector?.Clone(),
                Writer = jobConfig.ReaderSettingsGroup?.Writer?.Clone(),
                Verifier = jobConfig.ReaderSettingsGroup?.Verifier?.Clone()
            };

            config.ReaderSettings = readerSettings;

            // Set additional properties from parameters
            if (config is WriteStrategyConfiguration writeConfig)
            {
                if (jobConfig.Parameters.TryGetValue("accessPassword", out var password))
                    writeConfig.AccessPassword = password;

                if (jobConfig.Parameters.TryGetValue("useFastId", out var fastIdStr))
                    writeConfig.UseFastId = bool.TryParse(fastIdStr, out bool fastId) ? fastId : true;

                if (jobConfig.Parameters.TryGetValue("retryCount", out var retryStr) &&
                    int.TryParse(retryStr, out int retry))
                    writeConfig.RetryCount = retry;

                if (jobConfig.Parameters.TryGetValue("verifyWrites", out var verifyStr))
                    writeConfig.VerifyWrites = bool.TryParse(verifyStr, out bool verify) ? verify : true;

                if (jobConfig.Parameters.TryGetValue("writeTimeoutSeconds", out var timeoutStr) &&
                    int.TryParse(timeoutStr, out int timeout))
                    writeConfig.WriteTimeoutSeconds = timeout;

                if (jobConfig.Parameters.TryGetValue("enableLock", out var lockStr))
                    writeConfig.LockAfterWrite = bool.TryParse(lockStr, out bool lockVal) ? lockVal : false;

                if (jobConfig.Parameters.TryGetValue("enablePermalock", out var permalockStr))
                    writeConfig.PermalockAfterWrite = bool.TryParse(permalockStr, out bool permalock) ? permalock : false;
            }

            if (config is ReadOnlyStrategyConfiguration readOnlyConfig)
            {
                if (jobConfig.Parameters.TryGetValue("readDurationSeconds", out var durationStr) &&
                    int.TryParse(durationStr, out int duration))
                    readOnlyConfig.ReadDurationSeconds = duration;

                if (jobConfig.Parameters.TryGetValue("filterDuplicates", out var filterStr))
                    readOnlyConfig.FilterDuplicates = bool.TryParse(filterStr, out bool filter) ? filter : true;

                if (jobConfig.Parameters.TryGetValue("maxTagCount", out var maxTagsStr) &&
                    int.TryParse(maxTagsStr, out int maxTags))
                    readOnlyConfig.MaxTagCount = maxTags;
            }

            if (config is EnduranceTestConfiguration enduranceConfig)
            {
                if (jobConfig.Parameters.TryGetValue("maxCycles", out var cyclesStr) &&
                    int.TryParse(cyclesStr, out int cycles))
                    enduranceConfig.MaxCycles = cycles;

                if (jobConfig.Parameters.TryGetValue("testDurationSeconds", out var testDurationStr) &&
                    int.TryParse(testDurationStr, out int testDuration))
                    enduranceConfig.TestDurationSeconds = testDuration;

                // Add mapping for our new GPI/GPO parameters
                if (jobConfig.Parameters.TryGetValue("enableGpiTrigger", out var gpiTriggerStr))
                    enduranceConfig.EnableGpiTrigger = bool.TryParse(gpiTriggerStr, out bool gpiTrigger) ? gpiTrigger : false;

                if (jobConfig.Parameters.TryGetValue("gpiPort", out var gpiPortStr) &&
                    ushort.TryParse(gpiPortStr, out ushort gpiPort))
                    enduranceConfig.GpiPort = gpiPort;

                if (jobConfig.Parameters.TryGetValue("gpiTriggerState", out var gpiStateStr))
                    enduranceConfig.GpiTriggerState = bool.TryParse(gpiStateStr, out bool gpiState) ? gpiState : true;

                if (jobConfig.Parameters.TryGetValue("enableGpoOutput", out var gpoOutputStr))
                    enduranceConfig.EnableGpoOutput = bool.TryParse(gpoOutputStr, out bool gpoOutput) ? gpoOutput : false;

                if (jobConfig.Parameters.TryGetValue("gpoPort", out var gpoPortStr) &&
                    ushort.TryParse(gpoPortStr, out ushort gpoPort))
                    enduranceConfig.GpoPort = gpoPort;

                if (jobConfig.Parameters.TryGetValue("gpoVerificationTimeoutMs", out var timeoutStr) &&
                    int.TryParse(timeoutStr, out int timeout))
                    enduranceConfig.GpoVerificationTimeoutMs = timeout;
            }

            return config;
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