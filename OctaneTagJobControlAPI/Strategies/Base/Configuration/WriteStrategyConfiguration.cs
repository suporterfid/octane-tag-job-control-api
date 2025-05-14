namespace OctaneTagJobControlAPI.Strategies.Base.Configuration
{
    /// <summary>
    /// Configuration for write strategies
    /// </summary>
    public class WriteStrategyConfiguration : StrategyConfiguration
    {
        /// <summary>
        /// Access password for the tags
        /// </summary>
        public string AccessPassword { get; set; } = "00000000";

        /// <summary>
        /// Whether to use FastId for tag reading
        /// </summary>
        public bool UseFastId { get; set; } = true;

        /// <summary>
        /// Number of retry attempts for failed operations
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// Whether to verify writes after writing
        /// </summary>
        public bool VerifyWrites { get; set; } = true;

        /// <summary>
        /// Timeout for write operations in seconds
        /// </summary>
        public int WriteTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Whether to lock tags after writing
        /// </summary>
        public bool LockAfterWrite { get; set; } = false;

        /// <summary>
        /// Whether to permalock tags after writing
        /// </summary>
        public bool PermalockAfterWrite { get; set; } = false;
    }
}
