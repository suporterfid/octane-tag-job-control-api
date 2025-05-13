namespace OctaneTagJobControlAPI.JobStrategies.Base
{
    /// <summary>
    /// Metadata about a strategy
    /// </summary>
    public class StrategyMetadata
    {
        /// <summary>
        /// Name of the strategy
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Description of the strategy
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Category of the strategy (e.g., "Reading", "Writing", "Testing")
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Type of configuration required by the strategy
        /// </summary>
        public Type ConfigurationType { get; set; }

        /// <summary>
        /// Capabilities that the strategy supports
        /// </summary>
        public StrategyCapability Capabilities { get; set; }

        /// <summary>
        /// Whether the strategy requires multiple readers
        /// </summary>
        public bool RequiresMultipleReaders { get; set; }
    }
}
