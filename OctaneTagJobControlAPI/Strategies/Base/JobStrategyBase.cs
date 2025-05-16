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
        protected readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the JobStrategyBase class
        /// </summary>
        /// <param name="logFile">The path to the log file</param>
        /// <param name="settings">Dictionary of reader settings</param>
        protected JobStrategyBase(string logFile, Dictionary<string, ReaderSettings> settings, IServiceProvider serviceProvider, ILogger logger = null)
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

            // If logger is provided, use it; otherwise try to get generic ILogger from service provider
            _logger = logger;
            if (_logger == null && _serviceProvider != null)
            {
                try
                {
                    _logger = (ILogger)_serviceProvider.GetService(typeof(ILogger<JobStrategyBase>));
                }
                catch (Exception)
                {
                    Console.WriteLine("Error initilizing the logger in the JobStrategyBase");
                }
                
            }
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
                LogError(ex, "Error reporting tag data: {Message}", ex.Message);
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
        /// Log a message using the available logger or fallback to console if no logger is available
        /// </summary>
        /// <param name="level">The log level</param>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        protected void Log(LogLevel level, string message, params object[] args)
        {
            if (_logger != null)
            {
                _logger.Log(level, message, args);
            }
            else
            {
                // Fallback to console logging
                Console.WriteLine(string.Format(message, args));
            }
        }

        /// <summary>
        /// Log an information message
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        protected void LogInformation(string message, params object[] args)
        {
            Log(LogLevel.Information, message, args);
        }

        /// <summary>
        /// Log a warning message
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        protected void LogWarning(string message, params object[] args)
        {
            Log(LogLevel.Warning, message, args);
        }

        /// <summary>
        /// Log an error message
        /// </summary>
        /// <param name="exception">The exception that occurred</param>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        protected void LogError(Exception exception, string message, params object[] args)
        {
            if (_logger != null)
            {
                _logger.LogError(exception, message, args);
            }
            else
            {
                // Fallback to console logging
                Console.WriteLine($"ERROR: {string.Format(message, args)}");
                Console.WriteLine($"Exception: {exception.Message}");
                Console.WriteLine(exception.StackTrace);
            }
        }

        /// <summary>
        /// Log a debug message
        /// </summary>
        /// <param name="message">The message to log</param>
        /// <param name="args">Optional format arguments</param>
        protected void LogDebug(string message, params object[] args)
        {
            Log(LogLevel.Debug, message, args);
        }

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