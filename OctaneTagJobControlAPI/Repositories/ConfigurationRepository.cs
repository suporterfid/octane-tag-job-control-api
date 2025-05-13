// OctaneTagJobControlAPI/Repositories/ConfigurationRepository.cs
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services.Storage;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Repositories
{
    /// <summary>
    /// Repository implementation for configuration operations
    /// </summary>
    public class ConfigurationRepository : IConfigurationRepository
    {
        private readonly IStorageService _storage;
        private readonly ILogger<ConfigurationRepository> _logger;

        public ConfigurationRepository(IStorageService storage, ILogger<ConfigurationRepository> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<JobConfiguration> GetConfigurationAsync(string configId)
        {
            try
            {
                return await _storage.GetConfigurationAsync(configId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting configuration {ConfigId}", configId);
                return null;
            }
        }

        public async Task<List<JobConfiguration>> GetAllConfigurationsAsync()
        {
            try
            {
                return await _storage.GetAllConfigurationsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all configurations");
                return new List<JobConfiguration>();
            }
        }

        public async Task<string> CreateConfigurationAsync(JobConfiguration config)
        {
            try
            {
                // Generate new configuration ID if not provided
                string configId = config.Id ?? Guid.NewGuid().ToString();
                config.Id = configId;

                // Set creation timestamp if not set
                if (config.CreatedAt == default)
                {
                    config.CreatedAt = DateTime.UtcNow;
                }

                // Create a default log file path if not provided
                if (string.IsNullOrEmpty(config.LogFilePath))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string safeConfigName = new string(config.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                    config.LogFilePath = Path.Combine("Logs", $"{safeConfigName}_{timestamp}.csv");
                }

                // Save configuration
                await _storage.SaveConfigurationAsync(configId, config);

                return configId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating configuration");
                throw;
            }
        }

        public async Task<bool> UpdateConfigurationAsync(string configId, JobConfiguration config)
        {
            try
            {
                // Get existing config
                var existingConfig = await _storage.GetConfigurationAsync(configId);
                if (existingConfig == null)
                {
                    _logger.LogWarning("Cannot update non-existent configuration {ConfigId}", configId);
                    return false;
                }

                // Set ID to ensure it matches
                config.Id = configId;

                // Keep original creation timestamp
                config.CreatedAt = existingConfig.CreatedAt;

                // Update configuration
                await _storage.SaveConfigurationAsync(configId, config);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating configuration {ConfigId}", configId);
                return false;
            }
        }

        public async Task<bool> DeleteConfigurationAsync(string configId)
        {
            try
            {
                return await _storage.DeleteConfigurationAsync(configId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting configuration {ConfigId}", configId);
                return false;
            }
        }
    }
}