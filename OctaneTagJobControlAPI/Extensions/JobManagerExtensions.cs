using Microsoft.Extensions.DependencyInjection;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services;
using OctaneTagJobControlAPI.Strategies;
using OctaneTagJobControlAPI.Strategies.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Services
{
    /// <summary>
    /// Extension methods for JobManager to add R700 CAP specific functionality
    /// </summary>
    public static class JobManagerExtensions
    {
        /// <summary>
        /// Gets all active job IDs
        /// </summary>
        public static List<string> GetActiveJobIds(this JobManager jobManager)
        {
            var activeJobs = new List<string>();

            // Get currently active job ID
            var currentActiveJobId = jobManager.GetActiveJobId();
            if (!string.IsNullOrEmpty(currentActiveJobId))
            {
                activeJobs.Add(currentActiveJobId);
            }

            return activeJobs;
        }

        /// <summary>
        /// Tries to get the active strategy of a specific type
        /// </summary>
        /// <typeparam name="T">Type of strategy to retrieve</typeparam>
        /// <param name="jobManager">The job manager instance</param>
        /// <param name="strategy">Output parameter for the strategy if found</param>
        /// <returns>True if the strategy was found, otherwise false</returns>
        public static bool TryGetActiveStrategy<T>(this JobManager jobManager, out T strategy) where T : class, IJobStrategy
        {
            strategy = null;

            try
            {
                // Get the currently active job ID
                var activeJobId = jobManager.GetActiveJobId();
                if (string.IsNullOrEmpty(activeJobId))
                {
                    return false;
                }

                // Use reflection to access the private _jobStrategies field
                var jobStrategiesField = jobManager.GetType().GetField("_jobStrategies",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (jobStrategiesField == null)
                {
                    return false;
                }

                // Get the collection of job strategies
                var jobStrategies = jobStrategiesField.GetValue(jobManager) as
                    System.Collections.Concurrent.ConcurrentDictionary<string, IJobStrategy>;

                if (jobStrategies == null || !jobStrategies.TryGetValue(activeJobId, out var activeStrategy))
                {
                    return false;
                }

                // Check if the strategy is of the expected type
                if (activeStrategy is T typedStrategy)
                {
                    strategy = typedStrategy;
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
