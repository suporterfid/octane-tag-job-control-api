// OctaneTagJobControlAPI/Services/JobBackgroundService.cs
using OctaneTagJobControlAPI.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OctaneTagJobControlAPI.Services
{
    /// <summary>
    /// Background service for managing and monitoring jobs
    /// </summary>
    public class JobBackgroundService : BackgroundService
    {
        private readonly ILogger<JobBackgroundService> _logger;
        private readonly JobManager _jobManager;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(5);

        public JobBackgroundService(
            ILogger<JobBackgroundService> logger,
            JobManager jobManager)
        {
            _logger = logger;
            _jobManager = jobManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Job background service started");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Perform periodic job cleanup and monitoring
                    _jobManager.CleanupJobs();

                    // Log current job statuses
                    var statuses = await _jobManager.GetAllJobStatusesAsync();
                    var runningCount = statuses.Count(s => s.State == JobState.Running);

                    if (runningCount > 0)
                    {
                        _logger.LogInformation("Job status: {Total} total, {Running} running",
                            statuses.Count, runningCount);
                    }

                    // Wait for the next cleanup interval
                    await Task.Delay(_cleanupInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping the service
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in job background service");
            }
            finally
            {
                _logger.LogInformation("Job background service stopping");
            }
        }
    }
}