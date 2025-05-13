using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OctaneTagWritingTest
{
    public class ReaderSettingsManager
    {
        private static ReaderSettingsManager instance;
        private readonly string settingsDirectory;
        private const string DEFAULT_SETTINGS_DIR = "reader_settings";

        public static ReaderSettingsManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new ReaderSettingsManager();
                }
                return instance;
            }
        }

        private ReaderSettingsManager()
        {
            settingsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, DEFAULT_SETTINGS_DIR);
            Directory.CreateDirectory(settingsDirectory);
        }

        public void SaveSettings(ReaderSettings settings)
        {
            if (string.IsNullOrEmpty(settings.Name))
            {
                throw new ArgumentException("Settings name cannot be empty");
            }

            string filePath = GetSettingsFilePath(settings.Name);
            settings.Save(filePath);
        }

        public ReaderSettings LoadSettings(string name)
        {
            string filePath = GetSettingsFilePath(name);
            return ReaderSettings.Load(filePath);
        }

        public List<string> ListAvailableSettings()
        {
            return Directory.GetFiles(settingsDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .ToList();
        }

        public bool DeleteSettings(string name)
        {
            try
            {
                string filePath = GetSettingsFilePath(name);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string GetSettingsFilePath(string name)
        {
            return Path.Combine(settingsDirectory, $"{name}.json");
        }

        // For backward compatibility
        public ReaderSettings LoadLegacySettings()
        {
            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reader_settings.json");
            return ReaderSettings.Load(legacyPath);
        }

        public void SaveLegacySettings(ReaderSettings settings)
        {
            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reader_settings.json");
            settings.Save(legacyPath);
        }
    }
}
