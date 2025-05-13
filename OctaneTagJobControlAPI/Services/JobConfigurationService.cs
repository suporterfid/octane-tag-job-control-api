// OctaneTagJobControlAPI/Services/JobConfigurationService.cs
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Repositories;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Services
{
    /// <summary>
    /// Manages job configurations and provides default configurations
    /// </summary>
    public class JobConfigurationService
    {
        private readonly ILogger<JobConfigurationService> _logger;
        private readonly JobManager _jobManager;
        private readonly IConfigurationRepository _configRepository;

        public JobConfigurationService(
            ILogger<JobConfigurationService> logger,
            JobManager jobManager,
            IConfigurationRepository configRepository)
        {
            _logger = logger;
            _jobManager = jobManager;
            _configRepository = configRepository;
        }

        /// <summary>
        /// Initialize default configurations on startup
        /// </summary>
        public async Task InitializeDefaultConfigAsync()
        {
            try
            {
                // Create logs directory if it doesn't exist
                var logsDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                // Check if configurations exist
                var configs = await _configRepository.GetAllConfigurationsAsync();

                // If no configurations exist, create defaults
                if (configs == null || configs.Count == 0)
                {
                    await CreateDefaultConfigurationsAsync();
                }

                _logger.LogInformation("Job configuration service initialized with {Count} configurations",
                    configs?.Count ?? 0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing job configuration service");
            }
        }

        /// <summary>
        /// Get all available job configurations
        /// </summary>
        public async Task<List<JobConfiguration>> GetAllConfigurationsAsync()
        {
            return await _configRepository.GetAllConfigurationsAsync();
        }

        /// <summary>
        /// Get a specific job configuration
        /// </summary>
        public async Task<JobConfiguration> GetConfigurationAsync(string id)
        {
            return await _configRepository.GetConfigurationAsync(id);
        }

        /// <summary>
        /// Create a new job configuration
        /// </summary>
        public async Task<JobConfiguration> CreateConfigurationAsync(JobConfiguration config)
        {
            // Ensure the config has an ID
            if (string.IsNullOrEmpty(config.Id))
            {
                config.Id = Guid.NewGuid().ToString();
            }

            // Set a unique name if not provided
            if (string.IsNullOrEmpty(config.Name))
            {
                config.Name = $"Config_{config.Id.Substring(0, 8)}";
            }

            // Create a default log file path if not provided
            if (string.IsNullOrEmpty(config.LogFilePath))
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var safeConfigName = new string(config.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                config.LogFilePath = Path.Combine("Logs", $"{safeConfigName}_{timestamp}.csv");
            }

            // Store the configuration
            var configId = await _configRepository.CreateConfigurationAsync(config);

            return await _configRepository.GetConfigurationAsync(configId);
        }

        /// <summary>
        /// Update an existing job configuration
        /// </summary>
        public async Task<JobConfiguration> UpdateConfigurationAsync(string id, JobConfiguration config)
        {
            if (await _configRepository.GetConfigurationAsync(id) == null)
            {
                throw new KeyNotFoundException($"Configuration with ID {id} not found");
            }

            // Update the configuration
            config.Id = id;
            await _configRepository.UpdateConfigurationAsync(id, config);

            return await _configRepository.GetConfigurationAsync(id);
        }

        /// <summary>
        /// Delete a job configuration
        /// </summary>
        public async Task<bool> DeleteConfigurationAsync(string id)
        {
            return await _configRepository.DeleteConfigurationAsync(id);
        }

        #region Private Methods

        /// <summary>
        /// Create default job configurations
        /// </summary>
        private async Task CreateDefaultConfigurationsAsync()
        {
            try
            {
                // Create a basic configuration for each available strategy
                var strategies = _jobManager.GetAvailableStrategies();

                foreach (var strategy in strategies)
                {
                    var config = new JobConfiguration
                    {
                        Id = Guid.NewGuid().ToString(),
                        Name = $"Default_{strategy.Key}",
                        StrategyType = strategy.Key,
                        LogFilePath = Path.Combine("Logs", $"{strategy.Key}_Default.csv"),
                        Parameters = new Dictionary<string, string>
                        {
                            { "epcHeader", "E7" },
                            { "sku", "012345678901" }
                        },
                        ReaderSettings = new ReaderSettingsGroup
                        {
                            Detector = new ReaderSettings
                            {
                                Name = "detector",
                                Hostname = "192.168.68.248", // Default detector hostname
                                IncludeFastId = true,
                                IncludePeakRssi = true,
                                IncludeAntennaPortNumber = true,
                                ReportMode = "Individual",
                                RfMode = 0,
                                AntennaPort = 1,
                                TxPowerInDbm = 18,
                                MaxRxSensitivity = true,
                                RxSensitivityInDbm = -60,
                                SearchMode = "SingleTarget",
                                Session = 0,
                                MemoryBank = "Epc",
                                BitPointer = 32,
                                TagMask = "0017",
                                BitCount = 16,
                                FilterOp = "NotMatch",
                                FilterMode = "OnlyFilter1"
                            },
                            Writer = new ReaderSettings
                            {
                                Name = "writer",
                                Hostname = "192.168.1.100", // Default writer hostname
                                IncludeFastId = true,
                                IncludePeakRssi = true,
                                IncludeAntennaPortNumber = true,
                                ReportMode = "Individual",
                                RfMode = 1003,
                                AntennaPort = 1,
                                TxPowerInDbm = 33,
                                MaxRxSensitivity = true,
                                RxSensitivityInDbm = -70,
                                SearchMode = "DualTarget",
                                Session = 0,
                                MemoryBank = "Epc",
                                BitPointer = 32,
                                TagMask = "0017",
                                BitCount = 16,
                                FilterOp = "NotMatch",
                                FilterMode = "OnlyFilter1"
                            },
                            Verifier = new ReaderSettings
                            {
                                Name = "verifier",
                                Hostname = "192.168.68.93", // Default verifier hostname
                                IncludeFastId = true,
                                IncludePeakRssi = true,
                                IncludeAntennaPortNumber = true,
                                ReportMode = "Individual",
                                RfMode = 0,
                                AntennaPort = 1,
                                TxPowerInDbm = 33,
                                MaxRxSensitivity = true,
                                RxSensitivityInDbm = -90,
                                SearchMode = "SingleTarget",
                                Session = 0,
                                MemoryBank = "Epc",
                                BitPointer = 32,
                                TagMask = "0017",
                                BitCount = 16,
                                FilterOp = "NotMatch",
                                FilterMode = "OnlyFilter1"
                            }
                        }
                    };

                    await CreateConfigurationAsync(config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating default configurations");
            }
        }
        #endregion
    }
}