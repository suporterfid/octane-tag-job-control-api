using System.ComponentModel.DataAnnotations;

namespace OctaneTagJobControlAPI.Models
{
    /// <summary>
    /// Request model for starting a job
    /// </summary>
    public class StartJobRequest
    {
        /// <summary>
        /// Optional timeout in seconds for the job
        /// </summary>
        public int TimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Optional configuration ID to use for this job
        /// </summary>
        public string ConfigurationId { get; set; }
    }
}
