using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OctaneTagJobControlAPI.Strategies.Base;
using OctaneTagJobControlAPI.Models;
using OctaneTagWritingTest.Helpers;
using OctaneTagJobControlAPI.Services;

namespace OctaneTagJobControlAPI.Strategies.Base
{
    /// <summary>
    /// Base class for all job strategies
    /// </summary>
    public abstract class JobStrategyBase : IJobStrategy
    {
        private string _currentJobId;
        private JobManager _jobManager;
        protected readonly string logFile;
        protected readonly Dictionary<string, ReaderSettings> settings;
        protected CancellationToken cancellationToken;
        protected IServiceProvider _serviceProvider;

        /// <summary>
        /// Initializes a new instance of the JobStrategyBase class
        /// </summary>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="settings">Dictionary of reader settings</param>
        protected JobStrategyBase(string logFile, Dictionary<string, ReaderSettings> settings, IServiceProvider serviceProvider)
        {
            this.logFile = logFile;
            this.settings = settings;

            // Ensure log directory exists
            var logDirectory = Path.GetDirectoryName(logFile);
            if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            _serviceProvider = serviceProvider;
        }

        public void SetJobManager(JobManager jobManager)
        {
            _jobManager = jobManager;
        }

        public void SetJobId(string jobId)
        {
            _currentJobId = jobId;
        }

        public void SetServiceProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected void ReportTagData(TagReadData tagData)
        {
            if (string.IsNullOrEmpty(_currentJobId) || _serviceProvider == null) return;

            try
            {
                // Get JobManager from service provider when needed
                var jobManager = _serviceProvider.GetService<JobManager>();
                if (jobManager != null)
                {
                    tagData.JobId = _currentJobId;
                    jobManager.ReportTagData(_currentJobId, tagData);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                Console.WriteLine($"Error reporting tag data: {ex.Message}");
            }
        }

        /// <summary>
        /// Executes the job with the given cancellation token
        /// </summary>
        public abstract void RunJob(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current status of the job
        /// </summary>
        public abstract JobExecutionStatus GetStatus();

        /// <summary>
        /// Gets the strategy's metadata
        /// </summary>
        public abstract StrategyMetadata GetMetadata();

        /// <summary>
        /// Disposes of resources used by the strategy
        /// </summary>
        public virtual void Dispose()
        {
            // Base disposal logic
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Logs a line to the CSV file
        /// </summary>
        /// <param name="line">The line to log</param>
        protected virtual void LogToCsv(string line)
        {
            TagOpController.Instance.LogToCsv(logFile, line);
        }

        /// <summary>
        /// Checks if cancellation has been requested
        /// </summary>
        protected bool IsCancellationRequested()
        {
            return cancellationToken.IsCancellationRequested;
        }
    }
}