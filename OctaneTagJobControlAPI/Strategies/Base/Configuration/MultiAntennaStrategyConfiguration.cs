namespace OctaneTagJobControlAPI.Strategies.Base.Configuration
{
    /// <summary>
    /// Configuration for multi-antenna strategies
    /// </summary>
    public class MultiAntennaStrategyConfiguration : WriteStrategyConfiguration
    {
        /// <summary>
        /// Array of antenna ports to use
        /// </summary>
        public int[] AntennaPorts { get; set; } = new int[] { 1, 2 };

        /// <summary>
        /// Whether to optimize antenna switching
        /// </summary>
        public bool OptimizeAntennaSwitching { get; set; } = true;

        /// <summary>
        /// Method for selecting the best antenna
        /// </summary>
        public AntennaSelectionMethod AntennaSelectionMethod { get; set; } = AntennaSelectionMethod.RSSI;
    }
}
