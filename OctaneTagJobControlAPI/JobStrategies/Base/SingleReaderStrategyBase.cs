using System;
using System.Collections.Generic;
using Impinj.OctaneSdk;
using OctaneTagJobControlAPI.Models;
using Org.LLRP.LTK.LLRPV1;
using Org.LLRP.LTK.LLRPV1.Impinj;

namespace OctaneTagJobControlAPI.Strategies.Base
{
    /// <summary>
    /// Base class for strategies that use a single reader
    /// </summary>
    public abstract class SingleReaderStrategyBase : JobStrategyBase
    {
        protected readonly string hostname;
        protected ImpinjReader reader;
        protected string newAccessPassword = "00000000";

        /// <summary>
        /// Initializes a new instance of the SingleReaderStrategyBase class
        /// </summary>
        /// <param name="hostname">The hostname of the RFID reader</param>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="settings">Dictionary of reader settings</param>
        protected SingleReaderStrategyBase(
            string hostname,
            string logFile,
            Dictionary<string, ReaderSettings> settings)
            : base(logFile, settings)
        {
            this.hostname = hostname;
            reader = new ImpinjReader();
        }

        /// <summary>
        /// Configures the reader with the specified settings
        /// </summary>
        /// <param name="role">The role of the reader (e.g., "writer", "detector", "verifier")</param>
        /// <returns>The configured reader settings</returns>
        protected virtual Settings ConfigureReader(string role = "writer")
        {
            var readerSettings = GetSettingsForRole(role);

            try
            {
                // Connect to the reader
                reader.Connect(readerSettings.Hostname);
                reader.ApplyDefaultSettings();

                // Configure reader settings
                Settings settings = reader.QueryDefaultSettings();
                settings.Report.IncludeFastId = readerSettings.IncludeFastId;
                settings.Report.IncludePeakRssi = readerSettings.IncludePeakRssi;
                settings.Report.IncludeAntennaPortNumber = readerSettings.IncludeAntennaPortNumber;
                settings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), readerSettings.ReportMode);
                settings.RfMode = (uint)readerSettings.RfMode;

                // Configure antennas
                settings.Antennas.DisableAll();
                settings.Antennas.GetAntenna((ushort)readerSettings.AntennaPort).IsEnabled = true;
                settings.Antennas.GetAntenna((ushort)readerSettings.AntennaPort).TxPowerInDbm = readerSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna((ushort)readerSettings.AntennaPort).MaxRxSensitivity = readerSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna((ushort)readerSettings.AntennaPort).RxSensitivityInDbm = readerSettings.RxSensitivityInDbm;

                // Configure filters and search mode
                settings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), readerSettings.SearchMode);
                settings.Session = (ushort)readerSettings.Session;

                settings.Filters.TagFilter1.MemoryBank = (MemoryBank)Enum.Parse(typeof(MemoryBank), readerSettings.MemoryBank);
                settings.Filters.TagFilter1.BitPointer = (ushort)readerSettings.BitPointer;
                settings.Filters.TagFilter1.TagMask = readerSettings.TagMask;
                settings.Filters.TagFilter1.BitCount = readerSettings.BitCount;
                settings.Filters.TagFilter1.FilterOp = (TagFilterOp)Enum.Parse(typeof(TagFilterOp), readerSettings.FilterOp);
                settings.Filters.Mode = (TagFilterMode)Enum.Parse(typeof(TagFilterMode), readerSettings.FilterMode);

                // Enable low latency reporting
                EnableLowLatencyReporting(settings);

                // Apply the settings
                reader.ApplySettings(settings);

                return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configuring reader: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets the settings for a specific reader role
        /// </summary>
        /// <param name="role">The role of the reader (e.g., "writer", "detector", "verifier")</param>
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
        /// Enables low latency reporting for the reader
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
        /// Disposes of resources used by the strategy
        /// </summary>
        public override void Dispose()
        {
            CleanupReader();
            base.Dispose();
        }
    }
}