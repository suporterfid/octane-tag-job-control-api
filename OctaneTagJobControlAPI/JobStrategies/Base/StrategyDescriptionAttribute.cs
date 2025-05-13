namespace OctaneTagJobControlAPI.JobStrategies.Base
{
    /// <summary>
    /// Attribute to describe a strategy
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class StrategyDescriptionAttribute : Attribute
    {
        /// <summary>
        /// Description of the strategy
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Category of the strategy
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// Capabilities that the strategy supports
        /// </summary>
        public StrategyCapability Capabilities { get; }

        /// <summary>
        /// Initializes a new instance of the StrategyDescriptionAttribute class
        /// </summary>
        /// <param name="description">Description of the strategy</param>
        /// <param name="category">Category of the strategy</param>
        /// <param name="capabilities">Capabilities that the strategy supports</param>
        public StrategyDescriptionAttribute(
            string description,
            string category,
            StrategyCapability capabilities)
        {
            Description = description;
            Category = category;
            Capabilities = capabilities;
        }
    }
}
