// OctaneTagJobControlAPI/Extensions/ServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using OctaneTagJobControlAPI.Repositories;
using OctaneTagJobControlAPI.Services.Storage;

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
            // Add logger for strategy base classes
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

            // Add specific loggers for strategy classes
            services.AddSingleton<ILogger<OctaneTagJobControlAPI.Strategies.Base.JobStrategyBase>>();
            services.AddSingleton<ILogger<OctaneTagJobControlAPI.Strategies.Base.SingleReaderStrategyBase>>();
            services.AddSingleton<ILogger<OctaneTagJobControlAPI.Strategies.Base.MultiReaderStrategyBase>>();
            services.AddSingleton<ILogger<OctaneTagJobControlAPI.Strategies.MultiReaderEnduranceStrategy>>();

            // These types may need to be adjusted to match your actual strategy class names
            services.AddSingleton<ILogger<OctaneTagJobControlAPI.Strategies.ReadOnlyLoggingStrategy>>();
            // Add other strategy classes as needed

            return services;
        }
    }
}