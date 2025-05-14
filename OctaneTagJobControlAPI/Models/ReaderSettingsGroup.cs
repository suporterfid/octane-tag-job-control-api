using OctaneTagWritingTest;

namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Represents a group of RFID reader settings for different roles in the tag processing workflow.
    /// </summary>
    public class ReaderSettingsGroup
    {
        /// <summary>
        /// Gets or sets the settings for the detector reader, which is responsible for initially detecting and reading tags.
        /// </summary>
        public ReaderSettings Detector { get; set; } = new ReaderSettings();

        /// <summary>
        /// Gets or sets the settings for the writer reader, which is responsible for writing data to detected tags.
        /// </summary>
        public ReaderSettings Writer { get; set; } = new ReaderSettings();

        /// <summary>
        /// Gets or sets the settings for the verifier reader, which is responsible for verifying written tag data.
        /// </summary>
        public ReaderSettings Verifier { get; set; } = new ReaderSettings();
    }
}
