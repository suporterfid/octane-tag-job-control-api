namespace OctaneTagJobControlAPI.Models
{
    public class CreateJobRequest
    {
        public string Name { get; set; } = string.Empty;
        public string StrategyType { get; set; } = string.Empty;
        public string ConfigurationId { get; set; } = string.Empty;

        public ReaderSettingsGroup ReaderSettings { get; set; } = new ReaderSettingsGroup();
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
    }

    public class StartJobRequest
    {
        public string JobId { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; } = 300; // 5 minutes default timeout
    }
}
