using Impinj.OctaneSdk;
using OctaneTagWritingTest.Helpers;
using Org.LLRP.LTK.LLRPV1;
using Org.LLRP.LTK.LLRPV1.Impinj;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OctaneTagWritingTest
{
    /// <summary>
    /// Base class for all test strategies implementing common functionality
    /// </summary>
    public abstract class BaseTestStrategy : IJobStrategy
    {
        protected ImpinjReader reader;
        protected string hostname;
        protected string newAccessPassword = "00000000";
        protected string targetTid;  // Will be set with first TID read
        protected bool isTargetTidSet = false;
        protected string logFile;
        protected Stopwatch sw = new Stopwatch();
        protected CancellationToken cancellationToken;
        protected Dictionary<string, ReaderSettings> settings;

        /// <summary>
        /// Initializes a new instance of the BaseTestStrategy class
        /// </summary>
        /// <param name="hostname">The hostname of the RFID reader</param>
        /// <param name="logFile">The path to the log file for test results</param>
        /// <param name="readerSettings">Dictionary of reader settings by role</param>
        public BaseTestStrategy(string hostname, string logFile, Dictionary<string, ReaderSettings> readerSettings)
        {
            this.hostname = hostname;
            this.logFile = logFile;
            reader = new ImpinjReader();
            this.settings = readerSettings;
        }

        /// <summary>
        /// Gets the settings for a specific reader role
        /// </summary>
        /// <param name="role">The role of the reader (e.g., "writer", "verifier")</param>
        /// <returns>The settings for the specified role</returns>
        protected ReaderSettings GetSettingsForRole(string role)
        {
            if (!settings.ContainsKey(role))
            {
                throw new ArgumentException($"No settings found for role: {role}");
            }
            return settings[role];
        }

        /// <summary>
        /// Enables low latency reporting mode for the reader
        /// </summary>
        /// <param name="settings">The reader settings to modify</param>
        protected void EnableLowLatencyReporting(Settings settings)
        {
            MSG_ADD_ROSPEC addRoSpecMessage = reader.BuildAddROSpecMessage(settings);
            MSG_SET_READER_CONFIG setReaderConfigMessage = reader.BuildSetReaderConfigMessage(settings);
            setReaderConfigMessage.AddCustomParameter(new PARAM_ImpinjReportBufferConfiguration()
            {
                ReportBufferMode = ENUM_ImpinjReportBufferMode.Low_Latency
            });
            reader.ApplySettings(setReaderConfigMessage, addRoSpecMessage);
        }

        /// <summary>
        /// Appends a line to the CSV log file
        /// </summary>
        /// <param name="line">The line to append to the log file</param>
        protected void LogToCsv(string line)
        {
            File.AppendAllText(logFile, line + Environment.NewLine);
        }

        /// <summary>
        /// Configures the reader including connection, settings, and EPC list loading
        /// </summary>
        /// <returns>The configured reader settings</returns>
        /// <remarks>
        /// This method:
        /// - Loads the predefined EPC list
        /// - Connects to the reader
        /// - Applies default settings
        /// - Enables FastId and Individual reporting mode
        /// - Enables low latency reporting
        /// </remarks>
        protected virtual Settings ConfigureReader(string role = "writer")
        {
            EpcListManager.Instance.LoadEpcList("epc_list.txt");

            var roleSettings = GetSettingsForRole(role);
            reader.Connect(roleSettings.Hostname);
            reader.ApplyDefaultSettings();

            Settings readerSettings = reader.QueryDefaultSettings();
            readerSettings.Report.IncludeFastId = roleSettings.IncludeFastId;
            readerSettings.Report.IncludePeakRssi = roleSettings.IncludePeakRssi;
            readerSettings.Report.IncludePcBits = true;
            readerSettings.Report.IncludeAntennaPortNumber = roleSettings.IncludeAntennaPortNumber;
            readerSettings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), roleSettings.ReportMode);
            readerSettings.RfMode = (uint) roleSettings.RfMode;

            readerSettings.Antennas.DisableAll();
            readerSettings.Antennas.GetAntenna((ushort)roleSettings.AntennaPort).IsEnabled = true;
            readerSettings.Antennas.GetAntenna((ushort)roleSettings.AntennaPort).TxPowerInDbm = roleSettings.TxPowerInDbm;
            readerSettings.Antennas.GetAntenna((ushort)roleSettings.AntennaPort).MaxRxSensitivity = roleSettings.MaxRxSensitivity;
            readerSettings.Antennas.GetAntenna((ushort)roleSettings.AntennaPort).RxSensitivityInDbm = roleSettings.RxSensitivityInDbm;

            readerSettings.Antennas.GetAntenna(2).IsEnabled = true;
            readerSettings.Antennas.GetAntenna(2).TxPowerInDbm = 29;//roleSettings.TxPowerInDbm;
            readerSettings.Antennas.GetAntenna(2).MaxRxSensitivity = roleSettings.MaxRxSensitivity;
            readerSettings.Antennas.GetAntenna(2).RxSensitivityInDbm = roleSettings.RxSensitivityInDbm;

            readerSettings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), roleSettings.SearchMode);
            readerSettings.Session = (ushort)roleSettings.Session;

            readerSettings.Filters.TagFilter1.MemoryBank = (MemoryBank)Enum.Parse(typeof(MemoryBank), roleSettings.MemoryBank);
            readerSettings.Filters.TagFilter1.BitPointer = (ushort)roleSettings.BitPointer;
            readerSettings.Filters.TagFilter1.TagMask = roleSettings.TagMask;
            readerSettings.Filters.TagFilter1.BitCount = roleSettings.BitCount;
            readerSettings.Filters.TagFilter1.FilterOp = (TagFilterOp)Enum.Parse(typeof(TagFilterOp), roleSettings.FilterOp);
            readerSettings.Filters.Mode = (TagFilterMode)Enum.Parse(typeof(TagFilterMode), roleSettings.FilterMode);

            EnableLowLatencyReporting(readerSettings);
            reader.ApplySettings(readerSettings);

            return readerSettings;
        }


        /// <summary>
        /// Cleans up reader resources
        /// </summary>
        protected virtual void CleanupReader()
        {
            try
            {
                if (reader != null)
                {
                    reader.Stop();
                    reader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during reader cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if cancellation has been requested
        /// </summary>
        /// <returns>True if cancellation was requested, false otherwise</returns>
        protected bool IsCancellationRequested()
        {
            return cancellationToken.IsCancellationRequested;
        }

        /// <summary>
        /// Abstract method that each strategy will implement to execute its test
        /// </summary>
        public abstract void RunJob(CancellationToken cancellationToken = default);
    }
}
