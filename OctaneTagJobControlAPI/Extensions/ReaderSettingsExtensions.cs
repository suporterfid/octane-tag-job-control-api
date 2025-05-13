// OctaneTagJobControlAPI/Extensions/ReaderSettingsExtensions.cs
namespace OctaneTagJobControlAPI.Extensions
{
    public static class ReaderSettingsExtensions
    {
        /// <summary>
        /// Converts OctaneTagJobControlAPI.Models.ReaderSettings to OctaneTagWritingTest.ReaderSettings
        /// </summary>
        public static OctaneTagWritingTest.RfidDeviceSettings ToLegacySettings(
            this OctaneTagJobControlAPI.Models.ReaderSettings settings,
            string name = null)
        {
            return new OctaneTagWritingTest.RfidDeviceSettings
            {
                Name = name ?? settings.Name,
                Hostname = settings.Hostname,
                IncludeFastId = settings.IncludeFastId,
                IncludePeakRssi = settings.IncludePeakRssi,
                IncludeAntennaPortNumber = settings.IncludeAntennaPortNumber,
                ReportMode = settings.ReportMode,
                RfMode = settings.RfMode,
                AntennaPort = settings.AntennaPort,
                TxPowerInDbm = settings.TxPowerInDbm,
                MaxRxSensitivity = settings.MaxRxSensitivity,
                RxSensitivityInDbm = settings.RxSensitivityInDbm,
                SearchMode = settings.SearchMode,
                Session = settings.Session,
                MemoryBank = settings.MemoryBank,
                BitPointer = settings.BitPointer,
                TagMask = settings.TagMask,
                BitCount = settings.BitCount,
                FilterOp = settings.FilterOp,
                FilterMode = settings.FilterMode
            };
        }

        /// <summary>
        /// Converts OctaneTagWritingTest.ReaderSettings to OctaneTagJobControlAPI.Models.ReaderSettings
        /// </summary>
        public static OctaneTagJobControlAPI.Models.ReaderSettings ToApiSettings(
            this OctaneTagWritingTest.RfidDeviceSettings settings)
        {
            return new OctaneTagJobControlAPI.Models.ReaderSettings
            {
                Name = settings.Name,
                Hostname = settings.Hostname,
                IncludeFastId = settings.IncludeFastId,
                IncludePeakRssi = settings.IncludePeakRssi,
                IncludeAntennaPortNumber = settings.IncludeAntennaPortNumber,
                ReportMode = settings.ReportMode,
                RfMode = settings.RfMode,
                AntennaPort = settings.AntennaPort,
                TxPowerInDbm = settings.TxPowerInDbm,
                MaxRxSensitivity = settings.MaxRxSensitivity,
                RxSensitivityInDbm = settings.RxSensitivityInDbm,
                SearchMode = settings.SearchMode,
                Session = settings.Session,
                MemoryBank = settings.MemoryBank,
                BitPointer = settings.BitPointer,
                TagMask = settings.TagMask,
                BitCount = settings.BitCount,
                FilterOp = settings.FilterOp,
                FilterMode = settings.FilterMode
            };
        }
    }
}