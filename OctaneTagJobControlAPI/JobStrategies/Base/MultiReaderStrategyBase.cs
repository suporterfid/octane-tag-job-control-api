using System;
using System.Collections.Generic;
using Impinj.OctaneSdk;
using OctaneTagJobControlAPI.Models;
using Org.LLRP.LTK.LLRPV1;
using Org.LLRP.LTK.LLRPV1.Impinj;

namespace OctaneTagJobControlAPI.Strategies.Base
{
    /// <summary>
    /// Base class for strategies that use multiple readers
    /// </summary>
    public abstract class MultiReaderStrategyBase : JobStrategyBase
    {
        protected readonly string detectorHostname;
        protected readonly string writerHostname;
        protected readonly string verifierHostname;
        protected ImpinjReader detectorReader;
        protected ImpinjReader writerReader;
        protected ImpinjReader verifierReader;
        protected string newAccessPassword = "00000000";

        /// <summary>
        /// Initializes a new instance of the MultiReaderStrategyBase class
        /// </summary>
        /// <param name="detectorHostname">The hostname of the detector RFID reader</param>
        /// <param name="writerHostname">The hostname of the writer RFID reader</param>
        /// <param name="verifierHostname">The hostname of the verifier RFID reader</param>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="settings">Dictionary of reader settings</param>
        protected MultiReaderStrategyBase(
            string detectorHostname,
            string writerHostname,
            string verifierHostname,
            string logFile,
            Dictionary<string, ReaderSettings> settings)
            : base(logFile, settings)
        {
            this.detectorHostname = detectorHostname;
            this.writerHostname = writerHostname;
            this.verifierHostname = verifierHostname;

            detectorReader = new ImpinjReader();
            writerReader = new ImpinjReader();
            verifierReader = new ImpinjReader();
        }

        /// <summary>
        /// Configures the detector reader
        /// </summary>
        /// <returns>The configured detector reader settings</returns>
        protected virtual Settings ConfigureDetectorReader()
        {
            try
            {
                var detectorSettings = GetSettingsForRole("detector");

                detectorReader.Connect(detectorHostname);
                detectorReader.ApplyDefaultSettings();

                var settings = detectorReader.QueryDefaultSettings();
                settings.Report.IncludeFastId = detectorSettings.IncludeFastId;
                settings.Report.IncludePeakRssi = detectorSettings.IncludePeakRssi;
                settings.Report.IncludePcBits = true;
                settings.Report.IncludeAntennaPortNumber = detectorSettings.IncludeAntennaPortNumber;
                settings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), detectorSettings.ReportMode);
                settings.RfMode = (uint)detectorSettings.RfMode;

                settings.Antennas.DisableAll();
                settings.Antennas.GetAntenna((ushort)detectorSettings.AntennaPort).IsEnabled = true;
                settings.Antennas.GetAntenna((ushort)detectorSettings.AntennaPort).TxPowerInDbm = detectorSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna((ushort)detectorSettings.AntennaPort).MaxRxSensitivity = detectorSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna((ushort)detectorSettings.AntennaPort).RxSensitivityInDbm = detectorSettings.RxSensitivityInDbm;

                settings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), detectorSettings.SearchMode);
                settings.Session = (ushort)detectorSettings.Session;

                settings.Filters.TagFilter1.MemoryBank = (MemoryBank)Enum.Parse(typeof(MemoryBank), detectorSettings.MemoryBank);
                settings.Filters.TagFilter1.BitPointer = (ushort)detectorSettings.BitPointer;
                settings.Filters.TagFilter1.TagMask = detectorSettings.TagMask;
                settings.Filters.TagFilter1.BitCount = detectorSettings.BitCount;
                settings.Filters.TagFilter1.FilterOp = (TagFilterOp)Enum.Parse(typeof(TagFilterOp), detectorSettings.FilterOp);
                settings.Filters.Mode = (TagFilterMode)Enum.Parse(typeof(TagFilterMode), detectorSettings.FilterMode);

                EnableLowLatencyReporting(settings, detectorReader);
                detectorReader.ApplySettings(settings);

                return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configuring detector reader: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Configures the writer reader
        /// </summary>
        /// <returns>The configured writer reader settings</returns>
        protected virtual Settings ConfigureWriterReader()
        {
            try
            {
                var writerSettings = GetSettingsForRole("writer");

                writerReader.Connect(writerHostname);
                writerReader.ApplyDefaultSettings();

                var settings = writerReader.QueryDefaultSettings();
                settings.Report.IncludeFastId = writerSettings.IncludeFastId;
                settings.Report.IncludePeakRssi = writerSettings.IncludePeakRssi;
                settings.Report.IncludePcBits = true;
                settings.Report.IncludeAntennaPortNumber = writerSettings.IncludeAntennaPortNumber;
                settings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), writerSettings.ReportMode);
                settings.RfMode = (uint)writerSettings.RfMode;

                settings.Antennas.DisableAll();
                settings.Antennas.GetAntenna((ushort)writerSettings.AntennaPort).IsEnabled = true;
                settings.Antennas.GetAntenna((ushort)writerSettings.AntennaPort).TxPowerInDbm = writerSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna((ushort)writerSettings.AntennaPort).MaxRxSensitivity = writerSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna((ushort)writerSettings.AntennaPort).RxSensitivityInDbm = writerSettings.RxSensitivityInDbm;

                settings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), writerSettings.SearchMode);
                settings.Session = (ushort)writerSettings.Session;

                settings.Filters.TagFilter1.MemoryBank = (MemoryBank)Enum.Parse(typeof(MemoryBank), writerSettings.MemoryBank);
                settings.Filters.TagFilter1.BitPointer = (ushort)writerSettings.BitPointer;
                settings.Filters.TagFilter1.TagMask = writerSettings.TagMask;
                settings.Filters.TagFilter1.BitCount = writerSettings.BitCount;
                settings.Filters.TagFilter1.FilterOp = (TagFilterOp)Enum.Parse(typeof(TagFilterOp), writerSettings.FilterOp);
                settings.Filters.Mode = (TagFilterMode)Enum.Parse(typeof(TagFilterMode), writerSettings.FilterMode);

                EnableLowLatencyReporting(settings, writerReader);
                writerReader.ApplySettings(settings);

                return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configuring writer reader: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Configures the verifier reader
        /// </summary>
        /// <returns>The configured verifier reader settings</returns>
        protected virtual Settings ConfigureVerifierReader()
        {
            try
            {
                var verifierSettings = GetSettingsForRole("verifier");

                verifierReader.Connect(verifierHostname);
                verifierReader.ApplyDefaultSettings();

                var settings = verifierReader.QueryDefaultSettings();
                settings.Report.IncludeFastId = verifierSettings.IncludeFastId;
                settings.Report.IncludePeakRssi = verifierSettings.IncludePeakRssi;
                settings.Report.IncludePcBits = true;
                settings.Report.IncludeAntennaPortNumber = verifierSettings.IncludeAntennaPortNumber;
                settings.Report.Mode = (ReportMode)Enum.Parse(typeof(ReportMode), verifierSettings.ReportMode);
                settings.RfMode = (uint)verifierSettings.RfMode;

                settings.Antennas.DisableAll();
                settings.Antennas.GetAntenna((ushort)verifierSettings.AntennaPort).IsEnabled = true;
                settings.Antennas.GetAntenna((ushort)verifierSettings.AntennaPort).TxPowerInDbm = verifierSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna((ushort)verifierSettings.AntennaPort).MaxRxSensitivity = verifierSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna((ushort)verifierSettings.AntennaPort).RxSensitivityInDbm = verifierSettings.RxSensitivityInDbm;

                settings.SearchMode = (SearchMode)Enum.Parse(typeof(SearchMode), verifierSettings.SearchMode);
                settings.Session = (ushort)verifierSettings.Session;

                settings.Filters.TagFilter1.MemoryBank = (MemoryBank)Enum.Parse(typeof(MemoryBank), verifierSettings.MemoryBank);
                settings.Filters.TagFilter1.BitPointer = (ushort)verifierSettings.BitPointer;
                settings.Filters.TagFilter1.TagMask = verifierSettings.TagMask;
                settings.Filters.TagFilter1.BitCount = verifierSettings.BitCount;
                settings.Filters.TagFilter1.FilterOp = (TagFilterOp)Enum.Parse(typeof(TagFilterOp), verifierSettings.FilterOp);
                settings.Filters.Mode = (TagFilterMode)Enum.Parse(typeof(TagFilterMode), verifierSettings.FilterMode);

                EnableLowLatencyReporting(settings, verifierReader);
                verifierReader.ApplySettings(settings);

                return settings;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configuring verifier reader: {ex.Message}");
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
        /// Enables low latency reporting for the specified reader
        /// </summary>
        /// <param name="settings">The reader settings to modify</param>
        /// <param name="reader">The reader to apply settings to</param>
        protected void EnableLowLatencyReporting(Settings settings, ImpinjReader reader)
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
        /// Cleans up all reader resources
        /// </summary>
        protected virtual void CleanupReaders()
        {
            try
            {
                if (detectorReader != null)
                {
                    detectorReader.Stop();
                    detectorReader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during detector reader cleanup: {ex.Message}");
            }

            try
            {
                if (writerReader != null)
                {
                    writerReader.Stop();
                    writerReader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during writer reader cleanup: {ex.Message}");
            }

            try
            {
                if (verifierReader != null)
                {
                    verifierReader.Stop();
                    verifierReader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during verifier reader cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Disposes of resources used by the strategy
        /// </summary>
        public override void Dispose()
        {
            CleanupReaders();
            base.Dispose();
        }
    }
}