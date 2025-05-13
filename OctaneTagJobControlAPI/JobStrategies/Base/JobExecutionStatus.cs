namespace OctaneTagJobControlAPI.JobStrategies.Base
{
    /// <summary>
    /// Status of job execution
    /// </summary>
    public class JobExecutionStatus
    {
        /// <summary>
        /// Total number of tags processed
        /// </summary>
        public int TotalTagsProcessed { get; set; }

        /// <summary>
        /// Number of successfully processed tags
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Number of tags that failed processing
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Progress percentage (0-100)
        /// </summary>
        public double ProgressPercentage { get; set; }

        /// <summary>
        /// Current operation being performed
        /// </summary>
        public string CurrentOperation { get; set; }

        /// <summary>
        /// Total runtime of the job
        /// </summary>
        public TimeSpan RunTime { get; set; }

        /// <summary>
        /// Additional metrics specific to the strategy
        /// </summary>
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();
    }
}
