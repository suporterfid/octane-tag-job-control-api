using System;
using System.Collections.Generic;

namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Represents metrics for a specific RFID reader
    /// </summary>
    public class ReaderMetrics
    {
        /// <summary>
        /// The role of this reader (detector, writer, or verifier)
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// The hostname or IP address of the reader
        /// </summary>
        public string Hostname { get; set; }

        /// <summary>
        /// The reader's identifier
        /// </summary>
        public string ReaderID { get; set; }

        /// <summary>
        /// Number of unique tags read by this reader
        /// </summary>
        public int UniqueTagsRead { get; set; }

        /// <summary>
        /// Average write time in milliseconds (for writer role)
        /// </summary>
        public double AvgWriteTimeMs { get; set; }

        /// <summary>
        /// Average verify time in milliseconds (for verifier role)
        /// </summary>
        public double AvgVerifyTimeMs { get; set; }

        /// <summary>
        /// Number of locked tags (for writer role)
        /// </summary>
        public int LockedTags { get; set; }

        /// <summary>
        /// Number of successful operations
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Number of failed operations
        /// </summary>
        public int FailureCount { get; set; }

        /// <summary>
        /// Current read rate (tags per second)
        /// </summary>
        public double ReadRate { get; set; }

        /// <summary>
        /// Whether lock operations are enabled for this reader
        /// </summary>
        public bool LockEnabled { get; set; }

        /// <summary>
        /// Whether permalock operations are enabled for this reader
        /// </summary>
        public bool PermalockEnabled { get; set; }

        /// <summary>
        /// Additional reader-specific metrics
        /// </summary>
        public Dictionary<string, object> AdditionalMetrics { get; set; } = new Dictionary<string, object>();
    }
}
