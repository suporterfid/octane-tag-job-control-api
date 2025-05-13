// OctaneTagJobControlAPI/Services/Storage/FileStorageService.cs
using OctaneTagJobControlAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Services.Storage
{
    /// <summary>
    /// File-based storage service that persists data as JSON files
    /// </summary>
    public class FileStorageService : IStorageService, IDisposable
    {
        private readonly string _baseDirectory;
        private readonly ILogger<FileStorageService> _logger;
        private readonly SemaphoreSlim _fileSemaphore = new SemaphoreSlim(1, 1);
        private readonly JsonSerializerOptions _jsonOptions;

        // In-memory caches for performance
        private readonly ConcurrentDictionary<string, JobStatus> _jobStatusCache = new();
        private readonly ConcurrentDictionary<string, JobConfiguration> _configCache = new();
        private readonly ConcurrentDictionary<string, Dictionary<string, object>> _metricsCache = new();
        private readonly ConcurrentDictionary<string, List<string>> _logCache = new();

        // Flush control
        private readonly Timer _flushTimer;
        private readonly HashSet<string> _dirtyJobs = new();
        private readonly HashSet<string> _dirtyConfigs = new();
        private readonly HashSet<string> _dirtyMetrics = new();
        private readonly object _lockObject = new();

        // Directory paths
        private readonly string _jobsDirectory;
        private readonly string _configsDirectory;
        private readonly string _logsDirectory;
        private readonly string _metricsDirectory;

        public FileStorageService(ILogger<FileStorageService> logger, string baseDirectory = null)
        {
            _logger = logger;
            _baseDirectory = baseDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");

            // Set up subdirectories
            _jobsDirectory = Path.Combine(_baseDirectory, "Jobs");
            _configsDirectory = Path.Combine(_baseDirectory, "Configs");
            _logsDirectory = Path.Combine(_baseDirectory, "Logs");
            _metricsDirectory = Path.Combine(_baseDirectory, "Metrics");

            // Create JSON options
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            // Set up flush timer (every 30 seconds)
            _flushTimer = new Timer(FlushTimerCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public async Task InitializeAsync()
        {
            try
            {
                // Create directories if they don't exist
                Directory.CreateDirectory(_baseDirectory);
                Directory.CreateDirectory(_jobsDirectory);
                Directory.CreateDirectory(_configsDirectory);
                Directory.CreateDirectory(_logsDirectory);
                Directory.CreateDirectory(_metricsDirectory);

                // Load existing data into memory
                await LoadJobsAsync();
                await LoadConfigurationsAsync();

                _logger.LogInformation("Storage service initialized successfully. Loaded {JobCount} jobs and {ConfigCount} configurations.",
                    _jobStatusCache.Count, _configCache.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing storage service");
                throw;
            }
        }

        #region Job Status Operations

        public async Task<JobStatus> GetJobStatusAsync(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return null;

            // Try to get from cache first
            if (_jobStatusCache.TryGetValue(jobId, out var cachedStatus))
                return cachedStatus;

            // If not in cache, try to load from file
            var jobFile = GetJobFilePath(jobId);
            if (!File.Exists(jobFile))
                return null;

            try
            {
                await _fileSemaphore.WaitAsync();
                try
                {
                    var json = await File.ReadAllTextAsync(jobFile);
                    var status = JsonSerializer.Deserialize<JobStatus>(json, _jsonOptions);

                    if (status != null)
                    {
                        // Update cache
                        _jobStatusCache[jobId] = status;
                    }

                    return status;
                }
                finally
                {
                    _fileSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading job status for job {JobId}", jobId);
                return null;
            }
        }

        public async Task<List<JobStatus>> GetAllJobStatusesAsync()
        {
            try
            {
                // First, ensure all jobs are loaded into cache
                await LoadJobsAsync();

                // Return cached values
                return _jobStatusCache.Values.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all job statuses");
                return new List<JobStatus>();
            }
        }

        public async Task SaveJobStatusAsync(string jobId, JobStatus status)
        {
            if (string.IsNullOrEmpty(jobId) || status == null)
                return;

            try
            {
                // Update cache
                _jobStatusCache[jobId] = status;

                // Mark as dirty for delayed flush
                lock (_lockObject)
                {
                    _dirtyJobs.Add(jobId);
                }

                // If it's a completed state, flush immediately
                if (status.State == JobState.Completed ||
                    status.State == JobState.Failed ||
                    status.State == JobState.Canceled)
                {
                    await FlushJobAsync(jobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving job status for job {JobId}", jobId);
                throw;
            }
        }

        public async Task<bool> DeleteJobStatusAsync(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return false;

            try
            {
                // Remove from cache
                _jobStatusCache.TryRemove(jobId, out _);

                // Remove from dirty list
                lock (_lockObject)
                {
                    _dirtyJobs.Remove(jobId);
                }

                // Delete file
                var jobFile = GetJobFilePath(jobId);
                if (File.Exists(jobFile))
                {
                    await _fileSemaphore.WaitAsync();
                    try
                    {
                        File.Delete(jobFile);
                        return true;
                    }
                    finally
                    {
                        _fileSemaphore.Release();
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting job status for job {JobId}", jobId);
                return false;
            }
        }

        #endregion

        #region Configuration Operations

        public async Task<JobConfiguration> GetConfigurationAsync(string configId)
        {
            if (string.IsNullOrEmpty(configId))
                return null;

            // Try to get from cache first
            if (_configCache.TryGetValue(configId, out var cachedConfig))
                return cachedConfig;

            // If not in cache, try to load from file
            var configFile = GetConfigFilePath(configId);
            if (!File.Exists(configFile))
                return null;

            try
            {
                await _fileSemaphore.WaitAsync();
                try
                {
                    var json = await File.ReadAllTextAsync(configFile);
                    var config = JsonSerializer.Deserialize<JobConfiguration>(json, _jsonOptions);

                    if (config != null)
                    {
                        // Update cache
                        _configCache[configId] = config;
                    }

                    return config;
                }
                finally
                {
                    _fileSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configuration {ConfigId}", configId);
                return null;
            }
        }

        public async Task<List<JobConfiguration>> GetAllConfigurationsAsync()
        {
            try
            {
                // First, ensure all configurations are loaded into cache
                await LoadConfigurationsAsync();

                // Return cached values
                return _configCache.Values.ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all configurations");
                return new List<JobConfiguration>();
            }
        }

        public async Task SaveConfigurationAsync(string configId, JobConfiguration config)
        {
            if (string.IsNullOrEmpty(configId) || config == null)
                return;

            try
            {
                // Update cache
                _configCache[configId] = config;

                // Mark as dirty for delayed flush
                lock (_lockObject)
                {
                    _dirtyConfigs.Add(configId);
                }

                // Flush immediately since configurations change infrequently
                await FlushConfigAsync(configId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving configuration {ConfigId}", configId);
                throw;
            }
        }

        public async Task<bool> DeleteConfigurationAsync(string configId)
        {
            if (string.IsNullOrEmpty(configId))
                return false;

            try
            {
                // Remove from cache
                _configCache.TryRemove(configId, out _);

                // Remove from dirty list
                lock (_lockObject)
                {
                    _dirtyConfigs.Remove(configId);
                }

                // Delete file
                var configFile = GetConfigFilePath(configId);
                if (File.Exists(configFile))
                {
                    await _fileSemaphore.WaitAsync();
                    try
                    {
                        File.Delete(configFile);
                        return true;
                    }
                    finally
                    {
                        _fileSemaphore.Release();
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting configuration {ConfigId}", configId);
                return false;
            }
        }

        #endregion

        #region Log Operations

        public async Task<List<string>> GetJobLogEntriesAsync(string jobId, int maxEntries = 100)
        {
            if (string.IsNullOrEmpty(jobId))
                return new List<string>();

            // Try to get from cache first
            if (_logCache.TryGetValue(jobId, out var cachedLogs))
            {
                // Return the most recent entries up to maxEntries
                return cachedLogs.Skip(Math.Max(0, cachedLogs.Count - maxEntries)).ToList();
            }

            // If not in cache, try to load from file
            var logFile = GetLogFilePath(jobId);
            if (!File.Exists(logFile))
                return new List<string>();

            try
            {
                await _fileSemaphore.WaitAsync();
                try
                {
                    // Read log entries from file
                    var logs = await ReadLastLinesAsync(logFile, maxEntries);

                    // Update cache
                    _logCache[jobId] = logs;

                    return logs;
                }
                finally
                {
                    _fileSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading log entries for job {JobId}", jobId);
                return new List<string>();
            }
        }

        public async Task AppendJobLogEntryAsync(string jobId, string logEntry)
        {
            if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(logEntry))
                return;

            try
            {
                // Get log file path
                var logFile = GetLogFilePath(jobId);

                // Make sure the log directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(logFile));

                // Get or create log cache
                var logs = _logCache.GetOrAdd(jobId, _ => new List<string>());

                // Add log entry to cache (with timestamp)
                var timestampedEntry = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff UTC}: {logEntry}";
                logs.Add(timestampedEntry);

                // Ensure cache doesn't grow too large (keep last 1000 entries)
                const int maxCacheEntries = 1000;
                if (logs.Count > maxCacheEntries)
                {
                    logs.RemoveRange(0, logs.Count - maxCacheEntries);
                }

                // Append to file
                await _fileSemaphore.WaitAsync();
                try
                {
                    // Append to file (create if it doesn't exist)
                    using (var writer = new StreamWriter(logFile, append: true))
                    {
                        await writer.WriteLineAsync(timestampedEntry);
                    }
                }
                finally
                {
                    _fileSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error appending log entry for job {JobId}", jobId);
            }
        }

        #endregion

        #region Metrics Operations

        public async Task<Dictionary<string, object>> GetJobMetricsAsync(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
                return new Dictionary<string, object>();

            // Try to get from cache first
            if (_metricsCache.TryGetValue(jobId, out var cachedMetrics))
                return cachedMetrics;

            // If not in cache, try to load from file
            var metricsFile = GetMetricsFilePath(jobId);
            if (!File.Exists(metricsFile))
                return new Dictionary<string, object>();

            try
            {
                await _fileSemaphore.WaitAsync();
                try
                {
                    var json = await File.ReadAllTextAsync(metricsFile);
                    var metrics = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);

                    if (metrics != null)
                    {
                        // Update cache
                        _metricsCache[jobId] = metrics;
                    }

                    return metrics ?? new Dictionary<string, object>();
                }
                finally
                {
                    _fileSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading metrics for job {JobId}", jobId);
                return new Dictionary<string, object>();
            }
        }

        public async Task SaveJobMetricsAsync(string jobId, Dictionary<string, object> metrics)
        {
            if (string.IsNullOrEmpty(jobId) || metrics == null)
                return;

            try
            {
                // Update cache
                _metricsCache[jobId] = metrics;

                // Mark as dirty for delayed flush
                lock (_lockObject)
                {
                    _dirtyMetrics.Add(jobId);
                }

                // Metrics are updated frequently, so we'll rely on the timed flush
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving metrics for job {JobId}", jobId);
            }
        }

        #endregion

        #region Flush Operations

        public async Task FlushAsync()
        {
            var dirtyJobIds = new List<string>();
            var dirtyConfigIds = new List<string>();
            var dirtyMetricIds = new List<string>();

            // Get copies of dirty lists to avoid long lock
            lock (_lockObject)
            {
                dirtyJobIds.AddRange(_dirtyJobs);
                dirtyConfigIds.AddRange(_dirtyConfigs);
                dirtyMetricIds.AddRange(_dirtyMetrics);
            }

            // Flush jobs
            foreach (var jobId in dirtyJobIds)
            {
                await FlushJobAsync(jobId);
            }

            // Flush configurations
            foreach (var configId in dirtyConfigIds)
            {
                await FlushConfigAsync(configId);
            }

            // Flush metrics
            foreach (var metricId in dirtyMetricIds)
            {
                await FlushMetricsAsync(metricId);
            }
        }

        private async Task FlushJobAsync(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !_jobStatusCache.TryGetValue(jobId, out var status))
                return;

            try
            {
                // Get job file path
                var jobFile = GetJobFilePath(jobId);

                // Make sure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(jobFile));

                // Write to temp file then move (atomic operation)
                var tempFile = jobFile + ".tmp";

                await _fileSemaphore.WaitAsync();
                try
                {
                    // Serialize to temp file
                    var json = JsonSerializer.Serialize(status, _jsonOptions);
                    await File.WriteAllTextAsync(tempFile, json);

                    // Move temp file to final location (atomic)
                    File.Move(tempFile, jobFile, overwrite: true);

                    // Remove from dirty list
                    lock (_lockObject)
                    {
                        _dirtyJobs.Remove(jobId);
                    }
                }
                finally
                {
                    _fileSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing job status for job {JobId}", jobId);
            }
        }

        private async Task FlushConfigAsync(string configId)
        {
            if (string.IsNullOrEmpty(configId) || !_configCache.TryGetValue(configId, out var config))
                return;

            try
            {
                // Get config file path
                var configFile = GetConfigFilePath(configId);

                // Make sure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(configFile));

                // Write to temp file then move (atomic operation)
                var tempFile = configFile + ".tmp";

                await _fileSemaphore.WaitAsync();
                try
                {
                    // Serialize to temp file
                    var json = JsonSerializer.Serialize(config, _jsonOptions);
                    await File.WriteAllTextAsync(tempFile, json);

                    // Move temp file to final location (atomic)
                    File.Move(tempFile, configFile, overwrite: true);

                    // Remove from dirty list
                    lock (_lockObject)
                    {
                        _dirtyConfigs.Remove(configId);
                    }
                }
                finally
                {
                    _fileSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing configuration {ConfigId}", configId);
            }
        }

        private async Task FlushMetricsAsync(string jobId)
        {
            if (string.IsNullOrEmpty(jobId) || !_metricsCache.TryGetValue(jobId, out var metrics))
                return;

            try
            {
                // Get metrics file path
                var metricsFile = GetMetricsFilePath(jobId);

                // Make sure the directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(metricsFile));

                // Write to temp file then move (atomic operation)
                var tempFile = metricsFile + ".tmp";

                await _fileSemaphore.WaitAsync();
                try
                {
                    // Serialize to temp file
                    var json = JsonSerializer.Serialize(metrics, _jsonOptions);
                    await File.WriteAllTextAsync(tempFile, json);

                    // Move temp file to final location (atomic)
                    File.Move(tempFile, metricsFile, overwrite: true);

                    // Remove from dirty list
                    lock (_lockObject)
                    {
                        _dirtyMetrics.Remove(jobId);
                    }
                }
                finally
                {
                    _fileSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing metrics for job {JobId}", jobId);
            }
        }

        private void FlushTimerCallback(object state)
        {
            // Fire and forget - this runs on a timer thread
            _ = FlushAsync();
        }

        #endregion

        #region Loader Methods

        private async Task LoadJobsAsync()
        {
            try
            {
                if (!Directory.Exists(_jobsDirectory))
                    return;

                // Clear dirty jobs list
                lock (_lockObject)
                {
                    _dirtyJobs.Clear();
                }

                // Load all job files
                foreach (var file in Directory.GetFiles(_jobsDirectory, "*.json"))
                {
                    try
                    {
                        var jobId = Path.GetFileNameWithoutExtension(file);

                        // Skip if already in cache (except for forced reload)
                        if (_jobStatusCache.ContainsKey(jobId))
                            continue;

                        // Load job status
                        await _fileSemaphore.WaitAsync();
                        try
                        {
                            var json = await File.ReadAllTextAsync(file);
                            var status = JsonSerializer.Deserialize<JobStatus>(json, _jsonOptions);

                            if (status != null)
                            {
                                _jobStatusCache[jobId] = status;
                            }
                        }
                        finally
                        {
                            _fileSemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error loading job status from file {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading jobs");
            }
        }

        private async Task LoadConfigurationsAsync()
        {
            try
            {
                if (!Directory.Exists(_configsDirectory))
                    return;

                // Clear dirty configs list
                lock (_lockObject)
                {
                    _dirtyConfigs.Clear();
                }

                // Load all config files
                foreach (var file in Directory.GetFiles(_configsDirectory, "*.json"))
                {
                    try
                    {
                        var configId = Path.GetFileNameWithoutExtension(file);

                        // Skip if already in cache (except for forced reload)
                        if (_configCache.ContainsKey(configId))
                            continue;

                        // Load configuration
                        await _fileSemaphore.WaitAsync();
                        try
                        {
                            var json = await File.ReadAllTextAsync(file);
                            var config = JsonSerializer.Deserialize<JobConfiguration>(json, _jsonOptions);

                            if (config != null)
                            {
                                _configCache[configId] = config;
                            }
                        }
                        finally
                        {
                            _fileSemaphore.Release();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error loading configuration from file {File}", file);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading configurations");
            }
        }

        #endregion

        #region Helper Methods

        private string GetJobFilePath(string jobId)
        {
            return Path.Combine(_jobsDirectory, $"{jobId}.json");
        }

        private string GetConfigFilePath(string configId)
        {
            return Path.Combine(_configsDirectory, $"{configId}.json");
        }

        private string GetLogFilePath(string jobId)
        {
            return Path.Combine(_logsDirectory, $"{jobId}.log");
        }

        private string GetMetricsFilePath(string jobId)
        {
            return Path.Combine(_metricsDirectory, $"{jobId}.json");
        }

        private async Task<List<string>> ReadLastLinesAsync(string filePath, int maxLines)
        {
            if (!File.Exists(filePath))
                return new List<string>();

            var result = new List<string>();

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                if (maxLines <= 0)
                {
                    // Read all lines
                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        result.Add(line);
                    }

                    return result;
                }

                // Use a circular buffer to keep the last maxLines lines
                var ring = new string[maxLines];
                var ringIndex = 0;
                var lineCount = 0;

                string currentLine;
                while ((currentLine = await reader.ReadLineAsync()) != null)
                {
                    ring[ringIndex] = currentLine;
                    ringIndex = (ringIndex + 1) % maxLines;
                    lineCount++;
                }

                // Extract lines in the correct order
                if (lineCount <= maxLines)
                {
                    // Just return all lines in order
                    result.AddRange(ring.Take(lineCount));
                }
                else
                {
                    // Return lines in correct order (oldest to newest)
                    for (int i = 0; i < maxLines; i++)
                    {
                        var index = (ringIndex + i) % maxLines;
                        result.Add(ring[index]);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading last lines from file {FilePath}", filePath);
                return result;
            }
        }

        #endregion

        public void Dispose()
        {
            // Dispose of unmanaged resources
            _flushTimer?.Dispose();
            _fileSemaphore?.Dispose();

            // Ensure data is flushed
            FlushAsync().Wait();
        }
    }
}