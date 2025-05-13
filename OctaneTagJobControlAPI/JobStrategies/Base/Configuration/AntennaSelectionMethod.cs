namespace OctaneTagJobControlAPI.JobStrategies.Base.Configuration
{
    // <summary>
    /// Method for selecting the best antenna
    /// </summary>
    public enum AntennaSelectionMethod
    {
        /// <summary>
        /// Use the antenna with the strongest RSSI
        /// </summary>
        RSSI,

        /// <summary>
        /// Rotate through antennas in sequence
        /// </summary>
        Sequential,

        /// <summary>
        /// Use all antennas simultaneously
        /// </summary>
        Simultaneous
    }
}
