// OctaneTagJobControlAPI/Models/JobStatus.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Represents the current state of a job.
    /// </summary>
    public enum JobState
    {
        /// <summary>
        /// Job has been created but not yet started.
        /// </summary>
        NotStarted,

        /// <summary>
        /// Job is currently executing.
        /// </summary>
        Running,

        /// <summary>
        /// Job execution has been temporarily paused.
        /// </summary>
        Paused,

        /// <summary>
        /// Job has completed successfully.
        /// </summary>
        Completed,

        /// <summary>
        /// Job has failed during execution.
        /// </summary>
        Failed,

        /// <summary>
        /// Job was manually canceled.
        /// </summary>
        Canceled
    }

    /// <summary>
    /// Represents the current status and progress of a job.
    /// </summary>
    public class JobStatus
    {
        /// <summary>
        /// Gets or sets the unique identifier for the job.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the job.
        /// </summary>
        public string JobName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the associated configuration ID.
        /// </summary>
        public string ConfigurationId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the current state of the job.
        /// </summary>
        public JobState State { get; set; } = JobState.NotStarted;

        /// <summary>
        /// Gets or sets the time when the job started executing.
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// Gets or sets the time when the job finished executing.
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Gets or sets the description of the current operation being performed.
        /// </summary>
        public string CurrentOperation { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the overall progress percentage of the job.
        /// </summary>
        public double ProgressPercentage { get; set; } = 0;

        /// <summary>
        /// Gets or sets the total number of RFID tags processed.
        /// </summary>
        public int TotalTagsProcessed { get; set; } = 0;

        /// <summary>
        /// Gets or sets the number of successfully processed tags.
        /// </summary>
        public int SuccessCount { get; set; } = 0;

        /// <summary>
        /// Gets or sets the number of failed tag operations.
        /// </summary>
        public int FailureCount { get; set; } = 0;

        /// <summary>
        /// Gets or sets the path to the job's log file.
        /// </summary>
        public string LogFilePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the error message if the job failed.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets additional metrics collected during job execution.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, string> Metrics { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets whether the job has finished executing (completed, failed, or canceled).
        /// </summary>
        [JsonIgnore]
        public bool IsFinished =>
            State == JobState.Completed ||
            State == JobState.Failed ||
            State == JobState.Canceled;

        /// <summary>
        /// Gets whether the job is currently active (running or paused).
        /// </summary>
        [JsonIgnore]
        public bool IsActive =>
            State == JobState.Running ||
            State == JobState.Paused;
    }
}