// OctaneTagJobControlAPI/Extensions/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OctaneTagJobControlAPI.Repositories;
using OctaneTagJobControlAPI.Services.Storage;
using Serilog;

namespace OctaneTagJobControlAPI.Extensions
{
    /// <summary>
    /// Extension methods for service registration
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Add persistence services to the service collection
        /// </summary>
        public static IServiceCollection AddPersistenceServices(this IServiceCollection services, string dataDirectory = null)
        {
            // Add storage service
            services.AddStorageServices(dataDirectory);

            // Add repositories
            services.AddSingleton<IJobRepository, JobRepository>();
            services.AddSingleton<IConfigurationRepository, ConfigurationRepository>();

            return services;
        }

        /// <summary>
        /// Add logging services to the service collection for strategy classes
        /// </summary>
        public static IServiceCollection AddStrategyLogging(this IServiceCollection services)
        {
            // Add logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
                builder.AddSerilog();
            });

            return services;
        }
    }
}