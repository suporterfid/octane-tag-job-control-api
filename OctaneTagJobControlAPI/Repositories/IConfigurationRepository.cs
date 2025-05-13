// OctaneTagJobControlAPI/Repositories/IConfigurationRepository.cs
using OctaneTagJobControlAPI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Repositories
{
    /// <summary>
    /// Repository interface for configuration operations
    /// </summary>
    public interface IConfigurationRepository
    {
        Task<JobConfiguration> GetConfigurationAsync(string configId);
        Task<List<JobConfiguration>> GetAllConfigurationsAsync();
        Task<string> CreateConfigurationAsync(JobConfiguration config);
        Task<bool> UpdateConfigurationAsync(string configId, JobConfiguration config);
        Task<bool> DeleteConfigurationAsync(string configId);
    }
}