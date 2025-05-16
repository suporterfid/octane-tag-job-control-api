using OctaneTagJobControlAPI.Strategies;
using OctaneTagJobControlAPI.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OctaneTagJobControlAPI.Strategies.Base;

namespace OctaneTagJobControlAPI.Extensions
{
    /// <summary>
    /// Extension methods for setting up Impinj R700 CAP services
    /// </summary>
    public static class ImpinjR700Extensions
    {
        /// <summary>
        /// Adds Impinj R700 CAP services to the service collection
        /// </summary>
        public static IServiceCollection AddImpinjR700CapServices(this IServiceCollection services)
        {
            // Register services required for R700 CAP
            return services;
        }

        /// <summary>
        /// Registers the Impinj R700 CAP strategy with the strategy factory
        /// </summary>
        public static void RegisterImpinjR700CapStrategy(StrategyFactory strategyFactory)
        {
            // This registration is handled automatically by the StrategyFactory 
            // through reflection in the DiscoverStrategyTypes method
            // This method is here for potential future customization
        }

        /// <summary>
        /// Configure Impinj R700 CAP middleware
        /// </summary>
        public static IApplicationBuilder UseImpinjR700Cap(this IApplicationBuilder app, IConfiguration configuration)
        {
            // Load R700 CAP configuration
            var r700Config = new R700CapConfig();
            configuration.GetSection("ImpinjR700Cap").Bind(r700Config);

            // Configure middleware
            app.UseMiddleware<R700CapMiddleware>(r700Config);

            return app;
        }
    }

    /// <summary>
    /// Configuration for Impinj R700 CAP
    /// </summary>
    public class R700CapConfig
    {
        /// <summary>
        /// Hostname of the R700 reader
        /// </summary>
        public string ReaderHostname { get; set; } = "192.168.1.100";

        /// <summary>
        /// Reader ID for reporting
        /// </summary>
        public string ReaderID { get; set; } = "Tunnel-01";

        /// <summary>
        /// Whether to enable tag locking
        /// </summary>
        public bool EnableLock { get; set; } = true;

        /// <summary>
        /// Whether to enable permalocking
        /// </summary>
        public bool EnablePermalock { get; set; } = false;

        /// <summary>
        /// API key for securing the endpoints (optional)
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Middleware for Impinj R700 CAP API authorization
    /// </summary>
    public class R700CapMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly R700CapConfig _config;
        private readonly ILogger<R700CapMiddleware> _logger;

        /// <summary>
        /// Initializes a new instance of the R700CapMiddleware class
        /// </summary>
        public R700CapMiddleware(
            RequestDelegate next,
            R700CapConfig config,
            ILogger<R700CapMiddleware> logger)
        {
            _next = next;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Processes an HTTP request
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            // Check if this is an R700 CAP API request
            if (context.Request.Path.StartsWithSegments("/api/r700"))
            {
                // Check API key if configured
                if (!string.IsNullOrEmpty(_config.ApiKey))
                {
                    string apiKey = string.Empty;

                    // Try to get API key from header
                    if (context.Request.Headers.TryGetValue("X-API-KEY", out var headerValues))
                    {
                        apiKey = headerValues.FirstOrDefault();
                    }

                    // If API key not in header, try query string
                    if (string.IsNullOrEmpty(apiKey) &&
                        context.Request.Query.TryGetValue("apiKey", out var queryValues))
                    {
                        apiKey = queryValues.FirstOrDefault();
                    }

                    // Validate API key
                    if (apiKey != _config.ApiKey)
                    {
                        _logger.LogWarning("Unauthorized access attempt to R700 CAP API");
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Unauthorized: Invalid API key");
                        return;
                    }
                }
            }

            // Continue processing the request
            await _next(context);
        }
    }
}
