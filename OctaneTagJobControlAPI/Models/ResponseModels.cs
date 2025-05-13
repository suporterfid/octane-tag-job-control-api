namespace OctaneTagJobControlAPI.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }
    }

    public class JobCreatedResponse
    {
        public string JobId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string LogFilePath { get; set; } = string.Empty;
    }

    public class JobMetricsResponse
    {
        public string JobId { get; set; } = string.Empty;
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();
    }

    public class JobLogResponse
    {
        public string JobId { get; set; } = string.Empty;
        public List<string> LogEntries { get; set; } = new List<string>();
    }
}
