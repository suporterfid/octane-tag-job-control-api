using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OctaneTagJobControlAPI.Extensions;
using OctaneTagJobControlAPI.Services;
using OctaneTagJobControlAPI.Strategies;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI
{
    public static class ProgramExtensions
    {
        /// <summary>
        /// Configures all services for the RFID Job Control API application including new R700 CAP functionality
        /// </summary>
        public static void ConfigureAllServices(WebApplicationBuilder builder)
        {
            // Add R700 CAP services
            builder.Services.AddImpinjR700CapServices();

            // Configure other services as needed
        }

        /// <summary>
        /// Configures all middleware for the RFID Job Control API application including new R700 CAP functionality
        /// </summary>
        public static void ConfigureMiddleware(WebApplication app)
        {
            // Use R700 CAP middleware with configuration
            app.UseImpinjR700Cap(app.Configuration);
        }

        /// <summary>
        /// Initializes the R700 CAP environment
        /// </summary>
        public static async Task InitializeR700CapEnvironmentAsync(IServiceProvider serviceProvider)
        {
            try
            {
                // Load R700 configuration
                var configuration = serviceProvider.GetService<IConfiguration>();
                var r700Section = configuration.GetSection("ImpinjR700Cap");

                // Get required services
                var jobManager = serviceProvider.GetService<JobManager>();
                var jobConfigService = serviceProvider.GetService<JobConfigurationService>();

                if (jobManager != null && jobConfigService != null)
                {
                    // Check if we need to create a default R700 configuration
                    var configs = await jobConfigService.GetAllConfigurationsAsync();
                    bool hasR700Config = false;

                    foreach (var config in configs)
                    {
                        if (config.StrategyType == "ImpinjR700Cap")
                        {
                            hasR700Config = true;
                            break;
                        }
                    }

                    // Create default configuration if needed
                    if (!hasR700Config)
                    {
                        var defaultConfig = new Models.JobConfiguration
                        {
                            Name = "Default R700 CAP Config",
                            StrategyType = "ImpinjR700Cap",
                            Parameters = new Dictionary<string, string>
                            {
                                { "enableLock", r700Section.GetValue<bool>("EnableLock", true).ToString() },
                                { "enablePermalock", r700Section.GetValue<bool>("EnablePermalock", false).ToString() }
                            }
                        };

                        // Configure reader settings
                        defaultConfig.ReaderSettingsGroup = new Models.ReaderSettingsGroup
                        {
                            Writer = new Models.ReaderSettings
                            {
                                Name = "writer",
                                Hostname = r700Section.GetValue<string>("ReaderHostname", "192.168.1.100"),
                                IncludeFastId = true,
                                IncludePeakRssi = true,
                                IncludeAntennaPortNumber = true,
                                ReportMode = "Individual",
                                RfMode = 1003,
                                AntennaPort = 1,
                                TxPowerInDbm = 33,
                                MaxRxSensitivity = true,
                                RxSensitivityInDbm = -70,
                                SearchMode = "DualTarget",
                                Session = 0,
                                MemoryBank = "Epc",
                                BitPointer = 32,
                                TagMask = "0017",
                                BitCount = 16,
                                FilterOp = "NotMatch",
                                FilterMode = "OnlyFilter1",
                                Parameters = new Dictionary<string, string>
                                {
                                    { "ReaderID", r700Section.GetValue<string>("ReaderID", "Tunnel-01") }
                                }
                            }
                        };

                        // Create the configuration
                        await jobConfigService.CreateConfigurationAsync(defaultConfig);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing R700 CAP environment: {ex.Message}");
            }
        }
    }
}
