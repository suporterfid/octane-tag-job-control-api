﻿using System;
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
        /// <param name="logger">Logger for this strategy</param>
        protected MultiReaderStrategyBase(
            string detectorHostname,
            string writerHostname,
            string verifierHostname,
            string logFile,
            Dictionary<string, ReaderSettings> settings,
            string epcHeader = "E7",
            string sku = null,
            string encodingMethod = "BasicWithTidSuffix",
            int companyPrefixLength = 6,
            int itemReference = 0,
            IServiceProvider serviceProvider = null,
            ILogger logger = null)
            : base(logFile, settings, serviceProvider, logger)
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

                LogInformation("Connecting to detector reader at {Hostname}", detectorHostname);
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
                settings.Antennas.GetAntenna((ushort)detectorSettings.AntennaPort).MaxTxPower = false;
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

                LogInformation("Detector reader configured successfully with AntennaPort={AntennaPort}, TxPower={TxPower}dBm",
                    detectorSettings.AntennaPort, detectorSettings.TxPowerInDbm);


                return settings;
            }
            catch (Exception ex)
            {
                LogError(ex, "Error configuring detector reader at {Hostname}: {Message}", detectorHostname, ex.Message);
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

                LogInformation("Connecting to writer reader at {Hostname}", writerHostname);

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
                //settings.Antennas.GetAntenna((ushort)writerSettings.AntennaPort).IsEnabled = true;
                //settings.Antennas.GetAntenna((ushort)writerSettings.AntennaPort).TxPowerInDbm = writerSettings.TxPowerInDbm;
                //settings.Antennas.GetAntenna((ushort)writerSettings.AntennaPort).MaxRxSensitivity = writerSettings.MaxRxSensitivity;
                //settings.Antennas.GetAntenna((ushort)writerSettings.AntennaPort).RxSensitivityInDbm = writerSettings.RxSensitivityInDbm;

                settings.Antennas.GetAntenna(1).IsEnabled = true;
                settings.Antennas.GetAntenna(1).MaxTxPower = false;
                settings.Antennas.GetAntenna(1).TxPowerInDbm = writerSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna(1).MaxRxSensitivity = writerSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna(1).RxSensitivityInDbm = writerSettings.RxSensitivityInDbm;

                settings.Antennas.GetAntenna(2).IsEnabled = true;
                settings.Antennas.GetAntenna(2).MaxTxPower = false;
                settings.Antennas.GetAntenna(2).TxPowerInDbm = writerSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna(2).MaxRxSensitivity = writerSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna(2).RxSensitivityInDbm = writerSettings.RxSensitivityInDbm;

                settings.Antennas.GetAntenna(3).IsEnabled = true;
                settings.Antennas.GetAntenna(3).MaxTxPower = false;
                settings.Antennas.GetAntenna(3).TxPowerInDbm = writerSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna(3).MaxRxSensitivity = writerSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna(3).RxSensitivityInDbm = writerSettings.RxSensitivityInDbm;

                settings.Antennas.GetAntenna(4).IsEnabled = true;
                settings.Antennas.GetAntenna(4).MaxTxPower = false;
                settings.Antennas.GetAntenna(4).TxPowerInDbm = writerSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna(4).MaxRxSensitivity = writerSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna(4).RxSensitivityInDbm = writerSettings.RxSensitivityInDbm;


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

                LogInformation("Writer reader configured successfully with AntennaPort={AntennaPort}, TxPower={TxPower}dBm",
                    writerSettings.AntennaPort, writerSettings.TxPowerInDbm);

                return settings;
            }
            catch (Exception ex)
            {
                LogError(ex, "Error configuring writer reader at {Hostname}: {Message}", writerHostname, ex.Message);
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

                LogInformation("Connecting to verifier reader at {Hostname}", verifierHostname);
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

                settings.Antennas.GetAntenna(1).IsEnabled = true;
                settings.Antennas.GetAntenna(1).TxPowerInDbm = verifierSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna(1).MaxRxSensitivity = verifierSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna(1).RxSensitivityInDbm = verifierSettings.RxSensitivityInDbm;

                settings.Antennas.GetAntenna(2).IsEnabled = true;
                settings.Antennas.GetAntenna(2).TxPowerInDbm = verifierSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna(2).MaxRxSensitivity = verifierSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna(2).RxSensitivityInDbm = verifierSettings.RxSensitivityInDbm;

                settings.Antennas.GetAntenna(3).IsEnabled = true;
                settings.Antennas.GetAntenna(3).TxPowerInDbm = verifierSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna(3).MaxRxSensitivity = verifierSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna(3).RxSensitivityInDbm = verifierSettings.RxSensitivityInDbm;

                settings.Antennas.GetAntenna(4).IsEnabled = true;
                settings.Antennas.GetAntenna(4).TxPowerInDbm = verifierSettings.TxPowerInDbm;
                settings.Antennas.GetAntenna(4).MaxRxSensitivity = verifierSettings.MaxRxSensitivity;
                settings.Antennas.GetAntenna(4).RxSensitivityInDbm = verifierSettings.RxSensitivityInDbm;


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

                LogInformation("Verifier reader configured successfully with AntennaPort={AntennaPort}, TxPower={TxPower}dBm",
                    verifierSettings.AntennaPort, verifierSettings.TxPowerInDbm);

                return settings;
            }
            catch (Exception ex)
            {
                LogError(ex, "Error configuring verifier reader at {Hostname}: {Message}", verifierHostname, ex.Message);
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
                LogWarning("No settings found for role: {Role}", role);
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
            try
            {
                MSG_ADD_ROSPEC addRoSpecMessage = reader.BuildAddROSpecMessage(settings);
                MSG_SET_READER_CONFIG setReaderConfigMessage = reader.BuildSetReaderConfigMessage(settings);
                setReaderConfigMessage.AddCustomParameter(new PARAM_ImpinjReportBufferConfiguration()
                {
                    ReportBufferMode = ENUM_ImpinjReportBufferMode.Low_Latency
                });
                reader.ApplySettings(setReaderConfigMessage, addRoSpecMessage);
                LogDebug("Low latency reporting enabled for reader {ReaderName}", reader.Name ?? "unnamed");
            }
            catch (Exception ex)
            {
                LogError(ex, "Error enabling low latency reporting: {Message}", ex.Message);
            }

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
                    LogInformation("Stopping and disconnecting detector reader");
                    detectorReader.Stop();
                    detectorReader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "Error during detector reader cleanup: {Message}", ex.Message);
            }

            try
            {
                if (writerReader != null)
                {
                    LogInformation("Stopping and disconnecting writer reader");
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
                    LogInformation("Stopping and disconnecting verifier reader");
                    verifierReader.Stop();
                    verifierReader.Disconnect();
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "Error during verifier reader cleanup: {Message}", ex.Message);
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