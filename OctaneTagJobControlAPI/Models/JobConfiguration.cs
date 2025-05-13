// OctaneTagJobControlAPI/Models/JobConfiguration.cs
using System;
using System.Collections.Generic;

namespace OctaneTagJobControlAPI.Models
{
    public class JobConfiguration
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string StrategyType { get; set; } = string.Empty;
        public string LogFilePath { get; set; } = string.Empty;
        public ReaderSettingsGroup ReaderSettings { get; set; } = new ReaderSettingsGroup();
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    
}