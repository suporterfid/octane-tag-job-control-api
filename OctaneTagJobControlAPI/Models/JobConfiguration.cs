// OctaneTagJobControlAPI/Models/JobConfiguration.cs
using System;
using System.Collections.Generic;

namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Represents a job configuration for RFID job control.
    /// </summary>
    public class JobConfiguration
    {
        /// <summary>
        /// Gets or sets the unique identifier for the job configuration.
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Gets or sets the name of the job configuration.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the strategy type used in the job.
        /// </summary>
        public string StrategyType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the log file path for the job.
        /// </summary>
        public string LogFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the configuration identifier.
        /// </summary>
        public string ConfigurationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the reader settings group associated with the job.
        /// </summary>
        public ReaderSettingsGroup ReaderSettingsGroup { get; set; } = new ReaderSettingsGroup();

        /// <summary>
        /// Gets or sets the parameters for the job configuration.
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets or sets the creation date and time of the job configuration.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    
}
