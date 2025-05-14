namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Request model for creating a new RFID job.
    /// </summary>
    public class CreateJobRequest
    {
        /// <summary>
        /// Gets or sets the display name for the job.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the type of strategy to use for the job.
        /// Must match one of the available strategy types from the /api/job/strategies endpoint.
        /// </summary>
        public string StrategyType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the optional configuration ID to use as a template.
        /// If provided, the configuration will be loaded and merged with the provided settings.
        /// </summary>
        public string ConfigurationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the reader settings for detector, writer, and verifier readers.
        /// </summary>
        public ReaderSettingsGroup ReaderSettings { get; set; } = new ReaderSettingsGroup();

        /// <summary>
        /// Gets or sets strategy-specific parameters.
        /// The required parameters depend on the selected StrategyType.
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Request model for starting a job.
    /// </summary>
    public class StartJobRequest
    {
        /// <summary>
        /// Gets or sets the ID of the job to start.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the timeout in seconds for the job.
        /// Default is 300 seconds (5 minutes).
        /// </summary>
        public int TimeoutSeconds { get; set; } = 300;
    }
}
