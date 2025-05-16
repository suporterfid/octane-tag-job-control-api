using Microsoft.AspNetCore.Mvc;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services;
using OctaneTagJobControlAPI.Strategies;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OctaneTagJobControlAPI.Controllers
{
    /// <summary>
    /// Controller for Impinj R700 CAP Application
    /// </summary>
    [ApiController]
    [Route("api/r700")]
    public class ImpinjR700Controller : ControllerBase
    {
        private readonly ILogger<ImpinjR700Controller> _logger;
        private readonly JobManager _jobManager;

        // Constants for job IDs
        private const string R700_JOB_ID_PREFIX = "r700_cap_";
        private const string DEFAULT_JOB_NAME = "R700 CAP Job";

        /// <summary>
        /// Initializes a new instance of the ImpinjR700Controller class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="jobManager">The job manager service.</param>
        public ImpinjR700Controller(
            ILogger<ImpinjR700Controller> logger,
            JobManager jobManager)
        {
            _logger = logger;
            _jobManager = jobManager;
        }

        /// <summary>
        /// Scans tags in the RF field and returns information.
        /// </summary>
        /// <returns>List of tags currently in the RF field</returns>
        [HttpPost("read")]
        [ProducesResponseType(typeof(ImpinjR700ReadResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ReadTags()
        {
            try
            {
                // Get active job ID or create a new one if none exists
                string activeJobId = await EnsureActiveR700JobAsync();

                if (string.IsNullOrEmpty(activeJobId))
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Could not create or find an active R700 CAP job"
                    });
                }

                // Get the job status
                var jobStatus = await _jobManager.GetJobStatusAsync(activeJobId);
                if (jobStatus == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Job with ID {activeJobId} not found"
                    });
                }

                // Get all tag data using TagOpController helper methods
                var tagData = _jobManager.GetJobTagData(activeJobId);
                if (tagData == null || !tagData.Tags.Any())
                {
                    // Return empty result if no tags found
                    return Ok(new ImpinjR700ReadResponse
                    {
                        TagCount = 0,
                        Tags = new List<ImpinjR700TagInfo>()
                    });
                }

                // Convert to the expected response format
                var response = new ImpinjR700ReadResponse
                {
                    TagCount = tagData.UniqueCount,
                    Tags = tagData.Tags.Select(tag => new ImpinjR700TagInfo
                    {
                        EPC = tag.EPC,
                        TID = tag.TID,
                        EAN = tag.AdditionalData.ContainsKey("EAN") ? tag.AdditionalData["EAN"].ToString() : string.Empty,
                        AccessMemory = tag.AdditionalData.ContainsKey("AccessMemory") ? tag.AdditionalData["AccessMemory"].ToString() : "unlocked",
                        ReaderID = tag.AdditionalData.ContainsKey("ReaderID") ? tag.AdditionalData["ReaderID"].ToString() : string.Empty,
                        AntennaID = tag.AntennaPort,
                        RSSI = tag.RSSI
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading tags");
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error reading tags: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Encodes tags with new EPCs and access passwords, then locks them.
        /// </summary>
        /// <param name="writeRequests">List of tag write requests</param>
        /// <returns>Status of the write operations</returns>
        [HttpPost("write")]
        [ProducesResponseType(typeof(ImpinjR700WriteResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> WriteTags([FromBody] List<ImpinjR700WriteRequest> writeRequests)
        {
            try
            {
                if (writeRequests == null || !writeRequests.Any())
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "No write requests provided"
                    });
                }

                // Get active job ID or create a new one if none exists
                string activeJobId = await EnsureActiveR700JobAsync();

                if (string.IsNullOrEmpty(activeJobId))
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Could not create or find an active R700 CAP job"
                    });
                }

                // Validate the job status
                var jobStatus = await _jobManager.GetJobStatusAsync(activeJobId);
                if (jobStatus == null || jobStatus.State != JobState.Running)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Job with ID {activeJobId} is not in running state"
                    });
                }

                // Get the active strategy instance
                if (!_jobManager.TryGetActiveStrategy(out ImpinjR700CapStrategy strategy))
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Could not access the active R700 CAP strategy"
                    });
                }

                // Process each write request
                var tagResults = new List<ImpinjR700TagWriteResult>();
                foreach (var request in writeRequests)
                {
                    if (string.IsNullOrEmpty(request.TID) || string.IsNullOrEmpty(request.NewEPC))
                    {
                        tagResults.Add(new ImpinjR700TagWriteResult
                        {
                            TID = request.TID ?? "Invalid",
                            EPC = request.NewEPC ?? "Invalid",
                            WriteStatus = "Failed - Invalid Request"
                        });
                        continue;
                    }

                    // Queue the write operation using our strategy
                    strategy.WriteTagByTid(request.TID, request.NewEPC, request.AccessPassword);

                    // The strategy will handle the actual write operation when the tag is seen
                    // Add to results with pending status
                    tagResults.Add(new ImpinjR700TagWriteResult
                    {
                        TID = request.TID,
                        EPC = request.NewEPC,
                        WriteStatus = "Pending" // Actual status will be determined asynchronously
                    });
                }

                // Return a response with pending status
                var response = new ImpinjR700WriteResponse
                {
                    Status = "success",
                    Tags = tagResults
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error writing tags");
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error writing tags: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Gets status information about the R700 CAP job
        /// </summary>
        /// <returns>Job status information</returns>
        [HttpGet("status")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                // Check for active job with R700 prefix
                var activeJobId = FindR700JobId();

                if (string.IsNullOrEmpty(activeJobId))
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "No active R700 CAP job found"
                    });
                }

                // Get job status using existing job manager
                var jobStatus = await _jobManager.GetJobStatusAsync(activeJobId);
                if (jobStatus == null)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Job with ID {activeJobId} not found"
                    });
                }

                // Get metrics
                var metrics = await _jobManager.GetJobMetricsAsync(activeJobId);

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "R700 CAP status",
                    Data = new
                    {
                        JobId = activeJobId,
                        State = jobStatus.State.ToString(),
                        JobName = jobStatus.JobName,
                        StartTime = jobStatus.StartTime,
                        TotalTagsProcessed = jobStatus.TotalTagsProcessed,
                        SuccessCount = jobStatus.SuccessCount,
                        FailureCount = jobStatus.FailureCount,
                        CurrentOperation = jobStatus.CurrentOperation,
                        Metrics = metrics
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting R700 CAP status");
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error getting status: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Ensures an active R700 job is running
        /// </summary>
        /// <returns>The active job ID</returns>
        private async Task<string> EnsureActiveR700JobAsync()
        {
            // Check if an R700 job is already active
            var activeJobId = FindR700JobId();
            if (!string.IsNullOrEmpty(activeJobId))
            {
                var jobStatus = await _jobManager.GetJobStatusAsync(activeJobId);
                if (jobStatus != null && jobStatus.State == JobState.Running)
                {
                    return activeJobId; // Job is already running
                }
            }

            // Create a new R700 job
            return await CreateR700JobAsync();
        }

        /// <summary>
        /// Finds an existing R700 job ID
        /// </summary>
        /// <returns>The job ID if found, otherwise null</returns>
        private string FindR700JobId()
        {
            var activeJobId = _jobManager.GetActiveJobId();
            if (!string.IsNullOrEmpty(activeJobId) && activeJobId.StartsWith(R700_JOB_ID_PREFIX))
            {
                return activeJobId;
            }

            return null;
        }

        /// <summary>
        /// Creates a new R700 job
        /// </summary>
        /// <returns>The new job ID</returns>
        private async Task<string> CreateR700JobAsync()
        {
            try
            {
                // Create a unique job ID
                string jobId = $"{R700_JOB_ID_PREFIX}{DateTime.Now:yyyyMMddHHmmss}";

                // Get configuration from configuration service
                var configs = await _jobManager.GetAllConfigurationsAsync();
                var r700Config = configs.FirstOrDefault(c => c.StrategyType == "ImpinjR700Cap");

                if (r700Config == null)
                {
                    _logger.LogError("No R700 CAP configuration found");
                    return null;
                }

                // Register the job with the found configuration
                jobId = await _jobManager.RegisterJobAsync(r700Config);

                // Start the job
                bool success = await _jobManager.StartJobAsync(jobId, 86400); // 24 hour timeout
                if (!success)
                {
                    _logger.LogError("Failed to start R700 CAP job");
                    return null;
                }

                return jobId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating R700 CAP job");
                return null;
            }
        }
    }

    #region Model Classes

    /// <summary>
    /// Response model for tag reading operations
    /// </summary>
    public class ImpinjR700ReadResponse
    {
        /// <summary>
        /// Number of tags found
        /// </summary>
        public int TagCount { get; set; }

        /// <summary>
        /// List of tag information
        /// </summary>
        public List<ImpinjR700TagInfo> Tags { get; set; } = new List<ImpinjR700TagInfo>();
    }

    /// <summary>
    /// Tag information returned by read operations
    /// </summary>
    public class ImpinjR700TagInfo
    {
        /// <summary>
        /// Electronic Product Code
        /// </summary>
        public string EPC { get; set; }

        /// <summary>
        /// Tag Identifier
        /// </summary>
        public string TID { get; set; }

        /// <summary>
        /// European Article Number (barcode)
        /// </summary>
        public string EAN { get; set; }

        /// <summary>
        /// Status of access memory (locked/unlocked)
        /// </summary>
        public string AccessMemory { get; set; }

        /// <summary>
        /// Reader identifier
        /// </summary>
        public string ReaderID { get; set; }

        /// <summary>
        /// Antenna port number
        /// </summary>
        public ushort AntennaID { get; set; }

        /// <summary>
        /// Received signal strength indicator
        /// </summary>
        public double RSSI { get; set; }
    }

    /// <summary>
    /// Request model for tag writing operations
    /// </summary>
    public class ImpinjR700WriteRequest
    {
        /// <summary>
        /// Tag Identifier
        /// </summary>
        public string TID { get; set; }

        /// <summary>
        /// New Electronic Product Code to write
        /// </summary>
        public string NewEPC { get; set; }

        /// <summary>
        /// Access password to use for writing and locking
        /// </summary>
        public string AccessPassword { get; set; }
    }

    /// <summary>
    /// Response model for tag writing operations
    /// </summary>
    public class ImpinjR700WriteResponse
    {
        /// <summary>
        /// Overall status of the write operation
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// List of tag write results
        /// </summary>
        public List<ImpinjR700TagWriteResult> Tags { get; set; } = new List<ImpinjR700TagWriteResult>();
    }

    /// <summary>
    /// Result of a tag write operation
    /// </summary>
    public class ImpinjR700TagWriteResult
    {
        /// <summary>
        /// Tag Identifier
        /// </summary>
        public string TID { get; set; }

        /// <summary>
        /// Electronic Product Code written to the tag
        /// </summary>
        public string EPC { get; set; }

        /// <summary>
        /// Status of the write operation
        /// </summary>
        public string WriteStatus { get; set; }
    }

    #endregion
}
