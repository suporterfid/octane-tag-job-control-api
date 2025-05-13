// OctaneTagJobControlAPI/Services/Storage/StorageFactory.cs
using Microsoft.Extensions.DependencyInjection;
using System;

namespace OctaneTagJobControlAPI.Services.Storage
{
    /// <summary>
    /// Factory for creating storage services
    /// </summary>
    public static class StorageFactory
    {
        /// <summary>
        /// Adds storage services to the service collection
        /// </summary>
        public static IServiceCollection AddStorageServices(this IServiceCollection services, string dataDirectory = null)
        {
            // Register the file storage service as a singleton
            services.AddSingleton<IStorageService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<FileStorageService>>();
                var storage = new FileStorageService(logger, dataDirectory);

                // Initialize storage
                storage.InitializeAsync().GetAwaiter().GetResult();

                return storage;
            });

            return services;
        }
    }
}