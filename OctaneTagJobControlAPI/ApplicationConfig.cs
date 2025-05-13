using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OctaneTagWritingTest
{
    public class ApplicationConfig
    {
        // Reader network settings
        public string DetectorHostname { get; set; } = "192.168.68.248";
        public string WriterHostname { get; set; } = "192.168.68.94";
        public string VerifierHostname { get; set; } = "192.168.68.93";

        // Test parameters
        public string TestDescription { get; set; } = "Test";
        public string EpcHeader { get; set; } = "E7";
        public string EpcPlainItemCode { get; set; } = "1122334466";
        public string Sku { get; set; } = "012667712933";
        public long Quantity { get; set; } = 1;

        // Settings file path
        public string SettingsFilePath { get; set; } = "reader_settings.json";

        // Detector reader settings
        public int DetectorTxPowerInDbm { get; set; } = 18;
        public string DetectorSearchMode { get; set; } = "SingleTarget";
        public int DetectorSession { get; set; } = 0;
        public int DetectorRfMode { get; set; } = 0;
        public bool DetectorMaxRxSensitivity { get; set; } = false;
        public int DetectorRxSensitivityInDbm { get; set; } = -60;
        public string DetectorMemoryBank { get; set; } = "Epc";
        public int DetectorBitPointer { get; set; } = 32;
        public string DetectorTagMask { get; set; } = "0017";
        public int DetectorBitCount { get; set; } = 16;
        public string DetectorFilterOp { get; set; } = "NotMatch";
        public string DetectorFilterMode { get; set; } = "OnlyFilter1";

        // Writer reader settings
        public int WriterTxPowerInDbm { get; set; } = 33;
        public string WriterSearchMode { get; set; } = "SingleTarget";
        public int WriterSession { get; set; } = 0;
        public int WriterRfMode { get; set; } = 0;
        public bool WriterMaxRxSensitivity { get; set; } = false;
        public int WriterRxSensitivityInDbm { get; set; } = -70;
        public string WriterMemoryBank { get; set; } = "Epc";
        public int WriterBitPointer { get; set; } = 32;
        public string WriterTagMask { get; set; } = "0017";
        public int WriterBitCount { get; set; } = 16;
        public string WriterFilterOp { get; set; } = "NotMatch";
        public string WriterFilterMode { get; set; } = "OnlyFilter1";

        // Verifier reader settings
        public int VerifierTxPowerInDbm { get; set; } = 33;
        public string VerifierSearchMode { get; set; } = "SingleTarget";
        public int VerifierSession { get; set; } = 0;
        public int VerifierRfMode { get; set; } = 0;
        public bool VerifierMaxRxSensitivity { get; set; } = true;
        public int VerifierRxSensitivityInDbm { get; set; } = -90;
        public string VerifierMemoryBank { get; set; } = "Epc";
        public int VerifierBitPointer { get; set; } = 32;
        public string VerifierTagMask { get; set; } = "0017";
        public int VerifierBitCount { get; set; } = 16;
        public string VerifierFilterOp { get; set; } = "NotMatch";
        public string VerifierFilterMode { get; set; } = "OnlyFilter1";

        // Load configuration from JSON file
        public static ApplicationConfig LoadFromFile(string configPath)
        {
            if (!File.Exists(configPath))
                return new ApplicationConfig();

            string json = File.ReadAllText(configPath);
            return System.Text.Json.JsonSerializer.Deserialize<ApplicationConfig>(json);
        }

        // Save configuration to JSON file
        public void SaveToFile(string configPath)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }
    }
}
