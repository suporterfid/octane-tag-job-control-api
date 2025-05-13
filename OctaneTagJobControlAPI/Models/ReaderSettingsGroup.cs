using OctaneTagWritingTest;

namespace OctaneTagJobControlAPI.Models
{
    public class ReaderSettingsGroup
    {
        public ReaderSettings Detector { get; set; } = new ReaderSettings();
        public ReaderSettings Writer { get; set; } = new ReaderSettings();
        public ReaderSettings Verifier { get; set; } = new ReaderSettings();
    }
}
