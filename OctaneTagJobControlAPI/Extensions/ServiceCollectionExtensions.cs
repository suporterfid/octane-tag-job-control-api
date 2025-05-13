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
    }
}