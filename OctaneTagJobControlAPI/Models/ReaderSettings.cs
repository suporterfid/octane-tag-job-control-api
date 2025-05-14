using OctaneTagWritingTest;
using System.Text.Json;

namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Represents configuration settings for an RFID reader.
    /// </summary>
    public class ReaderSettings
    {
        private string name = string.Empty;

        /// <summary>
        /// Gets or sets the name of the reader settings configuration.
        /// Cannot be empty or whitespace.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when value is null, empty, or whitespace.</exception>
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

        /// <summary>
        /// Gets or sets the hostname or IP address of the reader.
        /// </summary>
        public string Hostname { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path to the reader's log file.
        /// </summary>
        public string LogFile { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether to include FastID in tag reports.
        /// </summary>
        public bool IncludeFastId { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to include peak RSSI values in tag reports.
        /// </summary>
        public bool IncludePeakRssi { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to include antenna port numbers in tag reports.
        /// </summary>
        public bool IncludeAntennaPortNumber { get; set; } = true;

        /// <summary>
        /// Gets or sets the report mode. Valid values: "Individual" or "BatchAfterStop".
        /// </summary>
        public string ReportMode { get; set; } = "Individual";

        /// <summary>
        /// Gets or sets the RF mode index for the reader.
        /// </summary>
        public int RfMode { get; set; } = 0;

        /// <summary>
        /// Gets or sets the antenna port number to use (typically 1-4).
        /// </summary>
        public int AntennaPort { get; set; } = 1;

        /// <summary>
        /// Gets or sets the transmit power in dBm (typically 10-30 dBm).
        /// </summary>
        public int TxPowerInDbm { get; set; } = 30;

        /// <summary>
        /// Gets or sets whether to use maximum receive sensitivity.
        /// </summary>
        public bool MaxRxSensitivity { get; set; } = true;

        /// <summary>
        /// Gets or sets the receive sensitivity in dBm when MaxRxSensitivity is false.
        /// </summary>
        public int RxSensitivityInDbm { get; set; } = -70;

        /// <summary>
        /// Gets or sets the search mode. Valid values: "SingleTarget", "DualTarget", or "TagFocus".
        /// </summary>
        public string SearchMode { get; set; } = "DualTarget";

        /// <summary>
        /// Gets or sets the Gen2 session number (0-3).
        /// </summary>
        public int Session { get; set; } = 0;

        /// <summary>
        /// Gets or sets the memory bank to read. Valid values: "Epc", "Tid", "User", "Reserved".
        /// </summary>
        public string MemoryBank { get; set; } = "Epc";

        /// <summary>
        /// Gets or sets the starting bit position for the tag mask.
        /// </summary>
        public int BitPointer { get; set; } = 32;

        /// <summary>
        /// Gets or sets the tag mask pattern in hexadecimal.
        /// </summary>
        public string TagMask { get; set; } = "0017";

        /// <summary>
        /// Gets or sets the number of bits in the tag mask to match.
        /// </summary>
        public int BitCount { get; set; } = 16;

        /// <summary>
        /// Gets or sets the filter operation. Valid values: "Match" or "NotMatch".
        /// </summary>
        public string FilterOp { get; set; } = "NotMatch";

        /// <summary>
        /// Gets or sets the filter mode. Valid values: "OnlyFilter1", "OnlyFilter2", or "Filter1AndFilter2".
        /// </summary>
        public string FilterMode { get; set; } = "OnlyFilter1";

        /// <summary>
        /// Creates a deep copy of the reader settings.
        /// </summary>
        /// <returns>A new instance of ReaderSettings with the same values.</returns>
        public ReaderSettings Clone()
        {
            return JsonSerializer.Deserialize<ReaderSettings>(
                JsonSerializer.Serialize(this)
            );
        }

        /// <summary>
        /// Saves the reader settings to a JSON file.
        /// </summary>
        /// <param name="filePath">The path where the settings file should be saved.</param>
        public void Save(string filePath)
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Loads reader settings from a JSON file.
        /// </summary>
        /// <param name="filePath">The path to the settings file.</param>
        /// <returns>A new ReaderSettings instance if the file exists; otherwise, null.</returns>
        public static ReaderSettings Load(string filePath)
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<ReaderSettings>(json);
            }
            return null;
        }

        /// <summary>
        /// Creates a new ReaderSettings instance with the specified name.
        /// </summary>
        /// <param name="name">The name for the reader settings.</param>
        /// <returns>A new ReaderSettings instance with default values and the specified name.</returns>
        public static ReaderSettings CreateNamed(string name)
        {
            return new ReaderSettings { Name = name };
        }
    }
}
