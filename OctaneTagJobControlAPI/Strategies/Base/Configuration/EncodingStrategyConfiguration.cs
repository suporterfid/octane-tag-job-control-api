namespace OctaneTagJobControlAPI.Strategies.Base.Configuration
{
    /// <summary>
    /// Configuration for encoding strategies
    /// </summary>
    public class EncodingStrategyConfiguration : WriteStrategyConfiguration
    {
        /// <summary>
        /// EPC header to use
        /// </summary>
        public string EpcHeader { get; set; } = "E7";

        /// <summary>
        /// SKU to use
        /// </summary>
        public string Sku { get; set; } = "012345678901";

        /// <summary>
        /// Method to use for encoding EPCs
        /// </summary>
        public string EncodingMethod { get; set; } = "BasicWithTidSuffix";

        /// <summary>
        /// Partition value for SGTIN-96 encoding
        /// </summary>
        public int PartitionValue { get; set; } = 6;

        /// <summary>
        /// Item reference for SGTIN-96 encoding
        /// </summary>
        public int ItemReference { get; set; } = 0;
    }
}