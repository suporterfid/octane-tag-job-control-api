using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using OctaneTagJobControlAPI.JobStrategies.Base.Configuration;
using OctaneTagJobControlAPI.JobStrategies.Base;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Strategies.Base;


namespace OctaneTagJobControlAPI.Strategies
{
    /// <summary>
    /// Factory for creating strategy instances
    /// </summary>
    public class StrategyFactory
    {
        private readonly ILogger<StrategyFactory> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Dictionary<string, Type> _strategyTypes;

        /// <summary>
        /// Initializes a new instance of the StrategyFactory class
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="serviceProvider">Service provider for dependency injection</param>
        public StrategyFactory(ILogger<StrategyFactory> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _strategyTypes = DiscoverStrategyTypes();
        }

        /// <summary>
        /// Get all available strategy metadata
        /// </summary>
        /// <returns>Collection of strategy metadata</returns>
        public IEnumerable<StrategyMetadata> GetAvailableStrategies()
        {
            return _strategyTypes.Values.Select(CreateMetadataForType);
        }

        /// <summary>
        /// Create a strategy instance from a configuration
        /// </summary>
        /// <param name="strategyName">Name of the strategy to create</param>
        /// <param name="config">Configuration for the strategy</param>
        /// <returns>The created strategy instance</returns>
        public IJobStrategy CreateStrategy(string strategyName, StrategyConfiguration config)
        {
            if (!_strategyTypes.TryGetValue(strategyName, out var strategyType))
            {
                throw new ArgumentException($"Strategy '{strategyName}' not found");
            }

            ValidateConfiguration(strategyType, config);

            try
            {
                // Create strategy based on its requirements
                if (typeof(MultiReaderStrategyBase).IsAssignableFrom(strategyType))
                {
                    _logger.LogInformation("Creating multi-reader strategy: {StrategyName}", strategyName);
                    return CreateMultiReaderStrategy(strategyType, config);
                }
                else if (strategyName == "CheckBoxStrategy")
                {
                    _logger.LogInformation("Creating CheckBox strategy");
                    return CreateCheckBoxStrategy(strategyType, config);
                }
                else
                {
                    _logger.LogInformation("Creating single-reader strategy: {StrategyName}", strategyName);
                    return CreateSingleReaderStrategy(strategyType, config);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating strategy {StrategyName}", strategyName);
                throw new ApplicationException($"Failed to create strategy '{strategyName}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Create a multi-reader strategy instance
        /// </summary>
        private IJobStrategy CreateMultiReaderStrategy(Type strategyType, StrategyConfiguration config)
        {
            return (IJobStrategy)Activator.CreateInstance(
                strategyType,
                config.ReaderSettings.Detector.Hostname,
                config.ReaderSettings.Writer.Hostname,
                config.ReaderSettings.Verifier.Hostname,
                config.LogFilePath,
                ConvertReaderSettings(config.ReaderSettings));
        }

        /// <summary>
        /// Create a CheckBox strategy instance
        /// </summary>
        private IJobStrategy CreateCheckBoxStrategy(Type strategyType, StrategyConfiguration config)
        {
            if (!(config is EncodingStrategyConfiguration encodingConfig))
            {
                throw new ArgumentException("CheckBoxStrategy requires EncodingStrategyConfiguration");
            }

            return (IJobStrategy)Activator.CreateInstance(
                strategyType,
                config.ReaderSettings.Writer.Hostname,
                config.LogFilePath,
                ConvertReaderSettings(config.ReaderSettings),
                encodingConfig.EpcHeader,
                encodingConfig.Sku,
                encodingConfig.EncodingMethod,
                encodingConfig.PartitionValue,
                encodingConfig.ItemReference);
        }

        /// <summary>
        /// Create a single-reader strategy instance
        /// </summary>
        private IJobStrategy CreateSingleReaderStrategy(Type strategyType, StrategyConfiguration config)
        {
            return (IJobStrategy)Activator.CreateInstance(
                strategyType,
                config.ReaderSettings.Writer.Hostname,
                config.LogFilePath,
                ConvertReaderSettings(config.ReaderSettings));
        }

        /// <summary>
        /// Discover all strategy types in the application
        /// </summary>
        private Dictionary<string, Type> DiscoverStrategyTypes()
        {
            var result = new Dictionary<string, Type>();

            try
            {
                // Find all classes implementing IJobStrategy
                var strategyTypes = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => a.GetTypes())
                    .Where(t => typeof(IJobStrategy).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                foreach (var type in strategyTypes)
                {
                    // Remove "Strategy" suffix for cleaner API names
                    string name = type.Name;
                    if (name.EndsWith("Strategy"))
                    {
                        name = name.Substring(0, name.Length - "Strategy".Length);
                    }

                    result[name] = type;
                    result[type.Name] = type; // Also allow full name for backward compatibility

                    _logger.LogInformation("Discovered strategy: {StrategyName} ({FullName})",
                        name, type.FullName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error discovering strategy types");
            }

            return result;
        }

        /// <summary>
        /// Validate that the configuration is appropriate for the strategy
        /// </summary>
        private void ValidateConfiguration(Type strategyType, StrategyConfiguration config)
        {
            var metadata = CreateMetadataForType(strategyType);

            // Check if the configuration type is compatible
            if (metadata.ConfigurationType != null &&
                !metadata.ConfigurationType.IsAssignableFrom(config.GetType()))
            {
                throw new ArgumentException(
                    $"Strategy '{metadata.Name}' requires configuration of type " +
                    $"'{metadata.ConfigurationType.Name}', but got '{config.GetType().Name}'");
            }

            // Check if multiple readers are required
            if (metadata.RequiresMultipleReaders)
            {
                if (string.IsNullOrEmpty(config.ReaderSettings.Detector?.Hostname))
                {
                    throw new ArgumentException(
                        $"Strategy '{metadata.Name}' requires a detector reader, " +
                        "but no detector hostname was provided");
                }

                if (string.IsNullOrEmpty(config.ReaderSettings.Writer?.Hostname))
                {
                    throw new ArgumentException(
                        $"Strategy '{metadata.Name}' requires a writer reader, " +
                        "but no writer hostname was provided");
                }

                if (string.IsNullOrEmpty(config.ReaderSettings.Verifier?.Hostname))
                {
                    throw new ArgumentException(
                        $"Strategy '{metadata.Name}' requires a verifier reader, " +
                        "but no verifier hostname was provided");
                }
            }
            else
            {
                // Single reader strategies require at least the writer hostname
                if (string.IsNullOrEmpty(config.ReaderSettings.Writer?.Hostname))
                {
                    throw new ArgumentException(
                        $"Strategy '{metadata.Name}' requires a writer reader, " +
                        "but no writer hostname was provided");
                }
            }
        }

        /// <summary>
        /// Create metadata for a strategy type
        /// </summary>
        private StrategyMetadata CreateMetadataForType(Type strategyType)
        {
            // Get strategy description from attribute
            var attribute = strategyType.GetCustomAttribute<StrategyDescriptionAttribute>();

            return new StrategyMetadata
            {
                Name = strategyType.Name,
                Description = attribute?.Description ?? "No description available",
                Category = attribute?.Category ?? "General",
                ConfigurationType = GetConfigurationTypeForStrategy(strategyType),
                Capabilities = attribute?.Capabilities ?? StrategyCapability.None,
                RequiresMultipleReaders = typeof(MultiReaderStrategyBase).IsAssignableFrom(strategyType)
            };
        }

        /// <summary>
        /// Get the configuration type for a strategy
        /// </summary>
        private Type GetConfigurationTypeForStrategy(Type strategyType)
        {
            // Map strategy types to appropriate configuration types
            string typeName = strategyType.Name;

            if (typeName.Contains("CheckBox"))
                return typeof(EncodingStrategyConfiguration);

            if (typeName.Contains("ReadOnly") || typeName.Contains("Reading"))
                return typeof(ReadOnlyStrategyConfiguration);

            if (typeName.Contains("MultiAntenna"))
                return typeof(MultiAntennaStrategyConfiguration);

            if (typeName.Contains("Endurance") || typeName.Contains("Robustness"))
                return typeof(EnduranceTestConfiguration);

            if (typeof(MultiReaderStrategyBase).IsAssignableFrom(strategyType))
                return typeof(WriteStrategyConfiguration);

            // Default to write strategy configuration
            return typeof(WriteStrategyConfiguration);
        }

        /// <summary>
        /// Convert API ReaderSettings to Dictionary format expected by strategies
        /// </summary>
        private Dictionary<string, ReaderSettings> ConvertReaderSettings(ReaderSettingsGroup settingsGroup)
        {
            var result = new Dictionary<string, ReaderSettings>();

            // Convert detector settings
            if (settingsGroup.Detector != null)
            {
                var detector = ConvertSingleReaderSettings(settingsGroup.Detector, "detector");
                result["detector"] = detector;
            }

            // Convert writer settings
            if (settingsGroup.Writer != null)
            {
                var writer = ConvertSingleReaderSettings(settingsGroup.Writer, "writer");
                result["writer"] = writer;
            }

            // Convert verifier settings
            if (settingsGroup.Verifier != null)
            {
                var verifier = ConvertSingleReaderSettings(settingsGroup.Verifier, "verifier");
                result["verifier"] = verifier;
            }

            return result;
        }

        /// <summary>
        /// Convert a single reader settings object
        /// </summary>
        private ReaderSettings ConvertSingleReaderSettings(
            OctaneTagJobControlAPI.Models.ReaderSettings source,
            string name)
        {
            return new ReaderSettings
            {
                Name = name,
                Hostname = source.Hostname,
                IncludeFastId = source.IncludeFastId,
                IncludePeakRssi = source.IncludePeakRssi,
                IncludeAntennaPortNumber = source.IncludeAntennaPortNumber,
                ReportMode = source.ReportMode,
                RfMode = source.RfMode,
                AntennaPort = source.AntennaPort,
                TxPowerInDbm = source.TxPowerInDbm,
                MaxRxSensitivity = source.MaxRxSensitivity,
                RxSensitivityInDbm = source.RxSensitivityInDbm,
                SearchMode = source.SearchMode,
                Session = source.Session,
                MemoryBank = source.MemoryBank,
                BitPointer = source.BitPointer,
                TagMask = source.TagMask,
                BitCount = source.BitCount,
                FilterOp = source.FilterOp,
                FilterMode = source.FilterMode
            };
        }
    }
}