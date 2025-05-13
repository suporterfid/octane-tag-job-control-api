// OctaneTagJobControlAPI/Services/Storage/IStorageService.cs
using OctaneTagJobControlAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Services.Storage
{
    /// <summary>
    /// Interface for storage service implementations
    /// </summary>
    public interface IStorageService
    {
        // Job operations
        Task<JobStatus> GetJobStatusAsync(string jobId);
        Task<List<JobStatus>> GetAllJobStatusesAsync();
        Task SaveJobStatusAsync(string jobId, JobStatus status);
        Task<bool> DeleteJobStatusAsync(string jobId);

        // Configuration operations
        Task<JobConfiguration> GetConfigurationAsync(string configId);
        Task<List<JobConfiguration>> GetAllConfigurationsAsync();
        Task SaveConfigurationAsync(string configId, JobConfiguration config);
        Task<bool> DeleteConfigurationAsync(string configId);

        // Log operations
        Task<List<string>> GetJobLogEntriesAsync(string jobId, int maxEntries = 100);
        Task AppendJobLogEntryAsync(string jobId, string logEntry);

        // Metric operations
        Task<Dictionary<string, object>> GetJobMetricsAsync(string jobId);
        Task SaveJobMetricsAsync(string jobId, Dictionary<string, object> metrics);

        // System operations
        Task InitializeAsync();
        Task FlushAsync();
    }
}