namespace OctaneTagJobControlAPI.JobStrategies.Base.Configuration
{
    /// <summary>
    /// Configuration for read-only strategies
    /// </summary>
    public class ReadOnlyStrategyConfiguration : StrategyConfiguration
    {
        /// <summary>
        /// Duration of the read operation in seconds
        /// </summary>
        public int ReadDurationSeconds { get; set; } = 30;

        /// <summary>
        /// Whether to filter duplicate reads
        /// </summary>
        public bool FilterDuplicates { get; set; } = true;

        /// <summary>
        /// Maximum number of tags to read (0 = unlimited)
        /// </summary>
        public int MaxTagCount { get; set; } = 0;
    }
}
