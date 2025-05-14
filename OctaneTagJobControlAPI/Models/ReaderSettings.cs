using OctaneTagWritingTest;
using System.Text.Json;

namespace OctaneTagJobControlAPI.Models
{
    public class ReaderSettings
    {
        private string name = string.Empty;
        public string Name
        {
            get => name;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Settings name cannot be empty or whitespace");
                name = value;
            }
        }
        public string Hostname { get; set; } = string.Empty;
        public string LogFile { get; set; } = string.Empty;
        public bool IncludeFastId { get; set; } = true;
        public bool IncludePeakRssi { get; set; } = true;
        public bool IncludeAntennaPortNumber { get; set; } = true;
        public string ReportMode { get; set; } = "Individual";
        public int RfMode { get; set; } = 0;
        public int AntennaPort { get; set; } = 1;
        public int TxPowerInDbm { get; set; } = 30;
        public bool MaxRxSensitivity { get; set; } = true;
        public int RxSensitivityInDbm { get; set; } = -70;
        public string SearchMode { get; set; } = "DualTarget";
        public int Session { get; set; } = 0;
        public string MemoryBank { get; set; } = "Epc";
        public int BitPointer { get; set; } = 32;
        public string TagMask { get; set; } = "0017";
        public int BitCount { get; set; } = 16;
        public string FilterOp { get; set; } = "NotMatch";
        public string FilterMode { get; set; } = "OnlyFilter1";

        public ReaderSettings Clone()
        {
            return JsonSerializer.Deserialize<ReaderSettings>(
                JsonSerializer.Serialize(this)
            );
        }

        public void Save(string filePath)
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        public static ReaderSettings Load(string filePath)
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<ReaderSettings>(json);
            }
            return null;
        }

        // Helper method to create settings with a name
        public static ReaderSettings CreateNamed(string name)
        {
            return new ReaderSettings { Name = name };
        }
    }
}
