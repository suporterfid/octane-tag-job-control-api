// Add to OctaneTagJobControlAPI/Extensions/ReaderSettingsExtensions.cs
using OctaneTagJobControlAPI.Models;

namespace OctaneTagJobControlAPI.Extensions
{
    public static class ReaderSettingsExtensions
    {
        public static Dictionary<string, ReaderSettings> ToDictionary(this ReaderSettingsGroup settingsGroup)
        {
            var result = new Dictionary<string, ReaderSettings>();

            if (settingsGroup.Detector != null)
                result["detector"] = settingsGroup.Detector;

            if (settingsGroup.Writer != null)
                result["writer"] = settingsGroup.Writer;

            if (settingsGroup.Verifier != null)
                result["verifier"] = settingsGroup.Verifier;

            return result;
        }
    }
}
