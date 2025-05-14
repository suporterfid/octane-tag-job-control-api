namespace OctaneTagJobControlAPI.Models
{
    public class TagReadData
    {
        public string JobId { get; set; }
        public string TID { get; set; }
        public string EPC { get; set; }
        public double RSSI { get; set; }
        public ushort AntennaPort { get; set; }
        public DateTime Timestamp { get; set; }
        public int ReadCount { get; set; }
        public string PcBits { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new Dictionary<string, object>();
    }

    public class TagDataResponse
    {
        public string JobId { get; set; }
        public List<TagReadData> Tags { get; set; } = new List<TagReadData>();
        public int TotalCount { get; set; }
        public int UniqueCount { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
