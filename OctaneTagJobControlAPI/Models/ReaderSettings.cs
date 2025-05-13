namespace OctaneTagJobControlAPI.Models
{
    public class ReaderSettings
    {
        public string Name { get; set; } = string.Empty;
        public string Hostname { get; set; } = string.Empty;
        public bool IncludeFastId { get; set; } = true;
        public bool IncludePeakRssi { get; set; } = true;
        public bool IncludeAntennaPortNumber { get; set; } = true;
        public string ReportMode { get; set; } = "Individual";
        public int RfMode { get; set; } = 0;
        public int AntennaPort { get; set; } = 1;
        public int TxPowerInDbm { get; set; } = 30;
        public bool MaxRxSensitivity { get; set; } = true;
        public int RxSensitivityInDbm { get; set; } = -70;
        public string SearchMode { get; set; } = "SingleTarget";
        public int Session { get; set; } = 0;
        public string MemoryBank { get; set; } = "Epc";
        public int BitPointer { get; set; } = 32;
        public string TagMask { get; set; } = "0017";
        public int BitCount { get; set; } = 16;
        public string FilterOp { get; set; } = "NotMatch";
        public string FilterMode { get; set; } = "OnlyFilter1";
    }
}
