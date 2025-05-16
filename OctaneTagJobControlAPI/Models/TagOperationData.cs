using Impinj.OctaneSdk;
using System;

namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Represents comprehensive tag operation data including original state, 
    /// expected changes, verification results, and metadata.
    /// </summary>
    public class TagOperationData
    {
        /// <summary>
        /// The Tag ID (TID) of the RFID tag
        /// </summary>
        public string TID { get; set; }

        /// <summary>
        /// The original EPC value before any operations
        /// </summary>
        public string OriginalEPC { get; set; }

        /// <summary>
        /// The expected EPC value after write operation
        /// </summary>
        public string ExpectedEPC { get; set; }

        /// <summary>
        /// The verified EPC value after write operation
        /// </summary>
        public string VerifiedEPC { get; set; }

        /// <summary>
        /// When the tag was first detected
        /// </summary>
        public DateTime FirstSeen { get; set; }

        /// <summary>
        /// When the tag was last detected
        /// </summary>
        public DateTime LastSeen { get; set; }

        /// <summary>
        /// The antenna port number that detected the tag
        /// </summary>
        public int AntennaPort { get; set; }

        /// <summary>
        /// The peak RSSI value in dBm
        /// </summary>
        public double RSSI { get; set; }

        /// <summary>
        /// Number of times the tag has been read
        /// </summary>
        public int ReadCount { get; set; }

        /// <summary>
        /// Whether the tag has been successfully verified after writing
        /// </summary>
        public bool IsVerified { get; set; }

        /// <summary>
        /// Current status of the tag operation (e.g., "Collected", "Written", "Verified", "Failed")
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// Any error message if the operation failed
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// The encoding method used for this tag
        /// </summary>
        public string EncodingMethod { get; set; }

        public Tag Tag { get; set; }

        /// <summary>
        /// Creates a new instance of TagOperationData
        /// </summary>
        public TagOperationData()
        {
            FirstSeen = DateTime.UtcNow;
            LastSeen = DateTime.UtcNow;
            Status = "Collected";
            ReadCount = 1;
        }

        /// <summary>
        /// Updates the tag's read statistics
        /// </summary>
        /// <param name="antennaPort">The antenna port that read the tag</param>
        /// <param name="rssi">The RSSI value of the read</param>
        public void UpdateReadStats(int antennaPort, double rssi)
        {
            LastSeen = DateTime.UtcNow;
            AntennaPort = antennaPort;
            RSSI = rssi;
            ReadCount++;
        }

        /// <summary>
        /// Updates the tag's verification status
        /// </summary>
        /// <param name="verifiedEpc">The EPC value read during verification</param>
        /// <returns>True if verification was successful, false otherwise</returns>
        public bool UpdateVerification(string verifiedEpc)
        {
            VerifiedEPC = verifiedEpc;
            IsVerified = string.Equals(ExpectedEPC, verifiedEpc, StringComparison.OrdinalIgnoreCase);
            Status = IsVerified ? "Verified" : "VerificationFailed";
            
            if (!IsVerified)
            {
                ErrorMessage = $"Verification failed: Expected EPC {ExpectedEPC} but got {verifiedEpc}";
            }

            return IsVerified;
        }
    }
}
