namespace OctaneTagJobControlAPI.Strategies.Base.Configuration
{
    /// <summary>
    /// Configuration for endurance test strategies
    /// </summary>
    public class EnduranceTestConfiguration : WriteStrategyConfiguration
    {
        /// <summary>
        /// Maximum number of cycles to run
        /// </summary>
        public int MaxCycles { get; set; } = 10000;

        /// <summary>
        /// Total test duration in seconds (0 = unlimited)
        /// </summary>
        public int TestDurationSeconds { get; set; } = 0;

        /// <summary>
        /// Whether to log cycle counts
        /// </summary>
        public bool LogCycleCounts { get; set; } = true;

        /// <summary>
        /// Interval for logging success counts
        /// </summary>
        public int SuccessCountLogIntervalSeconds { get; set; } = 5;

        public bool EnableGpiTrigger { get; set; } = false;
        public ushort GpiPort { get; set; } = 1;
        public bool GpiTriggerState { get; set; } = true;
        public bool EnableGpoOutput { get; set; } = false;
        public ushort GpoPort { get; set; } = 1;
        public int GpoVerificationTimeoutMs { get; set; } = 1000;
    }
}
