// OctaneTagJobControlAPI/Repositories/JobRepository.cs
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services.Storage;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Repositories
{
    /// <summary>
    /// Repository implementation for job operations
    /// </summary>
    public class JobRepository : IJobRepository
    {
        private readonly IStorageService _storage;
        private readonly ILogger<JobRepository> _logger;

        public JobRepository(IStorageService storage, ILogger<JobRepository> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<JobStatus> GetJobStatusAsync(string jobId)
        {
            try
            {
                return await _storage.GetJobStatusAsync(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting job status for job {JobId}", jobId);
                return null;
            }
        }

        public async Task<List<JobStatus>> GetAllJobStatusesAsync()
        {
            try
            {
                return await _storage.GetAllJobStatusesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all job statuses");
                return new List<JobStatus>();
            }
        }

        public async Task<string> CreateJobAsync(JobConfiguration config)
        {
            try
            {
                // Generate new job ID if not provided
                string jobId = config.Id ?? Guid.NewGuid().ToString();
                

                // Create a new job status
                var status = new JobStatus
                {
                    JobId = jobId,
                    JobName = config.Name,
                    State = JobState.NotStarted,
                    ConfigurationId = config.ConfigurationId,
                    StartTime = null,
                    EndTime = null,
                    CurrentOperation = "Initialized",
                    ProgressPercentage = 0,
                    TotalTagsProcessed = 0,
                    SuccessCount = 0,
                    FailureCount = 0,
                    LogFilePath = config.LogFilePath,
                    ErrorMessage = string.Empty,
                    Metrics = new Dictionary<string, string>()
                };

                // Save job status
                await _storage.SaveJobStatusAsync(jobId, status);

                // Create an initial metrics entry
                var metrics = new Dictionary<string, object>
                {
                    { "createdAt", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "strategyType", config.StrategyType },
                    { "configuration", config.Id }
                };

                await _storage.SaveJobMetricsAsync(jobId, metrics);

                // Log job creation
                await AddJobLogEntryAsync(jobId, $"Job created with name '{config.Name}' using strategy '{config.StrategyType}'");

                return jobId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job");
                throw;
            }
        }

        public async Task<bool> UpdateJobStatusAsync(string jobId, JobStatus status)
        {
            try
            {
                // Get existing status
                var existingStatus = await _storage.GetJobStatusAsync(jobId);
                if (existingStatus == null)
                {
                    _logger.LogWarning("Cannot update job status for non-existent job {JobId}", jobId);
                    return false;
                }

                // Update status
                await _storage.SaveJobStatusAsync(jobId, status);

                // Log status change if the state changed
                if (existingStatus.State != status.State)
                {
                    await AddJobLogEntryAsync(jobId, $"Job state changed from '{existingStatus.State}' to '{status.State}'");
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating job status for job {JobId}", jobId);
                return false;
            }
        }

        public async Task<bool> DeleteJobAsync(string jobId)
        {
            try
            {
                return await _storage.DeleteJobStatusAsync(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting job {JobId}", jobId);
                return false;
            }
        }

        public async Task<List<string>> GetJobLogEntriesAsync(string jobId, int maxEntries = 100)
        {
            try
            {
                return await _storage.GetJobLogEntriesAsync(jobId, maxEntries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting log entries for job {JobId}", jobId);
                return new List<string>();
            }
        }

        public async Task AddJobLogEntryAsync(string jobId, string logEntry)
        {
            try
            {
                await _storage.AppendJobLogEntryAsync(jobId, logEntry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding log entry for job {JobId}", jobId);
            }
        }

        public async Task<Dictionary<string, object>> GetJobMetricsAsync(string jobId)
        {
            try
            {
                return await _storage.GetJobMetricsAsync(jobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting metrics for job {JobId}", jobId);
                return new Dictionary<string, object>();
            }
        }

        public async Task UpdateJobMetricsAsync(string jobId, Dictionary<string, object> metrics)
        {
            try
            {
                await _storage.SaveJobMetricsAsync(jobId, metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating metrics for job {JobId}", jobId);
            }
        }
    }
}