namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Generic wrapper for API responses providing consistent structure across all endpoints.
    /// </summary>
    /// <typeparam name="T">The type of data contained in the response.</typeparam>
    public class ApiResponse<T>
    {
        /// <summary>
        /// Gets or sets whether the API request was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets a human-readable message describing the result.
        /// Contains error details when Success is false.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the response payload.
        /// May be null when Success is false.
        /// </summary>
        public T? Data { get; set; }
    }

    /// <summary>
    /// Response model returned when a new job is successfully created.
    /// </summary>
    public class JobCreatedResponse
    {
        /// <summary>
        /// Gets or sets the unique identifier of the created job.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the display name of the created job.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the path to the job's log file.
        /// </summary>
        public string LogFilePath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response model containing job performance metrics.
    /// Returned by the /api/job/{jobId}/metrics endpoint.
    /// </summary>
    public class JobMetricsResponse
    {
        /// <summary>
        /// Gets or sets the ID of the job these metrics belong to.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the collection of metrics.
        /// Keys are metric names and values are the corresponding measurements.
        /// </summary>
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Response model containing job log entries.
    /// Returned by the /api/job/{jobId}/logs endpoint.
    /// </summary>
    public class JobLogResponse
    {
        /// <summary>
        /// Gets or sets the ID of the job these logs belong to.
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the list of log entries.
        /// Entries are ordered from oldest to newest.
        /// </summary>
        public List<string> LogEntries { get; set; } = new List<string>();
    }
}
