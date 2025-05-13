// OctaneTagJobControlAPI/Repositories/IJobRepository.cs
using OctaneTagJobControlAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Repositories
{
    /// <summary>
    /// Repository interface for job operations
    /// </summary>
    public interface IJobRepository
    {
        Task<JobStatus> GetJobStatusAsync(string jobId);
        Task<List<JobStatus>> GetAllJobStatusesAsync();
        Task<string> CreateJobAsync(JobConfiguration config);
        Task<bool> UpdateJobStatusAsync(string jobId, JobStatus status);
        Task<bool> DeleteJobAsync(string jobId);
        Task<List<string>> GetJobLogEntriesAsync(string jobId, int maxEntries = 100);
        Task AddJobLogEntryAsync(string jobId, string logEntry);
        Task<Dictionary<string, object>> GetJobMetricsAsync(string jobId);
        Task UpdateJobMetricsAsync(string jobId, Dictionary<string, object> metrics);
    }
}