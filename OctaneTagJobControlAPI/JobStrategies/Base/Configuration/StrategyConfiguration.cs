using OctaneTagJobControlAPI.Models;

namespace OctaneTagJobControlAPI.JobStrategies.Base.Configuration
{
    /// <summary>
    /// Base configuration for all strategies
    /// </summary>
    public abstract class StrategyConfiguration
    {
        /// <summary>
        /// Path to the log file
        /// </summary>
        public string LogFilePath { get; set; }

        /// <summary>
        /// Reader settings for all readers
        /// </summary>
        public ReaderSettingsGroup ReaderSettings { get; set; } = new ReaderSettingsGroup();
    }
}
