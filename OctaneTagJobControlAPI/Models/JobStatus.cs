// OctaneTagJobControlAPI/Models/JobStatus.cs
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OctaneTagJobControlAPI.Models
{
    public enum JobState
    {
        NotStarted,
        Running,
        Paused,
        Completed,
        Failed,
        Canceled
    }

    public class JobStatus
    {
        public string JobId { get; set; } = string.Empty;
        public string JobName { get; set; } = string.Empty;
        public string ConfigurationId { get; set; } = string.Empty;
        public JobState State { get; set; } = JobState.NotStarted;
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string CurrentOperation { get; set; } = string.Empty;
        public double ProgressPercentage { get; set; } = 0;
        public int TotalTagsProcessed { get; set; } = 0;
        public int SuccessCount { get; set; } = 0;
        public int FailureCount { get; set; } = 0;
        public string LogFilePath { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;

        [JsonIgnore]
        public Dictionary<string, string> Metrics { get; set; } = new Dictionary<string, string>();

        [JsonIgnore]
        public bool IsFinished =>
            State == JobState.Completed ||
            State == JobState.Failed ||
            State == JobState.Canceled;

        [JsonIgnore]
        public bool IsActive =>
            State == JobState.Running ||
            State == JobState.Paused;
    }
}