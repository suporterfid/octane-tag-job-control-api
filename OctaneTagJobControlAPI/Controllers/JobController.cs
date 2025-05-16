using Microsoft.AspNetCore.Mvc;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services;
using OctaneTagJobControlAPI.Repositories;
using System.Text.Json;

namespace OctaneTagJobControlAPI.Controllers
{
    /// <summary>
    /// Controller for managing RFID job operations.
    /// </summary>
    [Route("api/[controller]")]
    public class JobController : ControllerBase
    {
        private readonly ILogger<JobController> _logger;
        private readonly JobManager _jobManager;
        private readonly JobConfigurationService _configService;
        private readonly IJobRepository _jobRepository;

        // Error message constants
        private const string JOB_ALREADY_RUNNING_ERROR = "Another job is currently running. Only one job can be active at a time.";

        /// <summary>
        /// Initializes a new instance of the <see cref="JobController"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="jobManager">The job manager service.</param>
        /// <param name="configService">The job configuration service.</param>
        public JobController(
            ILogger<JobController> logger,
            JobManager jobManager,
            JobConfigurationService configService,
            IJobRepository jobRepository)
        {
            _logger = logger;
            _jobManager = jobManager;
            _configService = configService;
            _jobRepository = jobRepository;
        }

        /// <summary>
        /// Gets all jobs with optional sorting parameters.
        /// </summary>
        /// <param name="sortBy">Sort criteria: "date" (default), "name", "status", or "progress".</param>
        /// <param name="runningFirst">If true, running jobs will be listed first.</param>
        /// <returns>A sorted list of job statuses.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(List<JobStatus>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllJobs(
    [FromQuery] string sortBy = "date",
    [FromQuery] bool runningFirst = true)
        {
            var jobs = await _jobManager.GetAllJobStatusesAsync();

            // If runningFirst is true, separate running jobs from non-running jobs
            List<JobStatus> sortedJobs;

            if (runningFirst)
            {
                var runningJobs = jobs.Where(j => j.State == JobState.Running).ToList();
                var otherJobs = jobs.Where(j => j.State != JobState.Running).ToList();

                // Apply sorting within each group
                switch (sortBy.ToLower())
                {
                    case "name":
                        runningJobs = runningJobs.OrderBy(j => j.JobName).ToList();
                        otherJobs = otherJobs.OrderBy(j => j.JobName).ToList();
                        break;
                    case "status":
                        runningJobs = runningJobs.OrderBy(j => j.State.ToString()).ToList();
                        otherJobs = otherJobs.OrderBy(j => j.State.ToString()).ToList();
                        break;
                    case "progress":
                        runningJobs = runningJobs.OrderByDescending(j => j.ProgressPercentage).ToList();
                        otherJobs = otherJobs.OrderByDescending(j => j.ProgressPercentage).ToList();
                        break;
                    case "date":
                    default:
                        runningJobs = runningJobs.OrderByDescending(j => j.StartTime ?? DateTime.MinValue).ToList();
                        otherJobs = otherJobs.OrderByDescending(j => j.StartTime ?? DateTime.MinValue).ToList();
                        break;
                }

                // Combine the lists: running jobs first, then the rest
                sortedJobs = runningJobs.Concat(otherJobs).ToList();
            }
            else
            {
                // Apply sorting to all jobs without separating them
                switch (sortBy.ToLower())
                {
                    case "name":
                        sortedJobs = jobs.OrderBy(j => j.JobName).ToList();
                        break;
                    case "status":
                        sortedJobs = jobs.OrderBy(j => j.State.ToString()).ToList();
                        break;
                    case "progress":
                        sortedJobs = jobs.OrderByDescending(j => j.ProgressPercentage).ToList();
                        break;
                    case "date":
                    default:
                        sortedJobs = jobs.OrderByDescending(j => j.StartTime ?? DateTime.MinValue).ToList();
                        break;
                }
            }

            return Ok(sortedJobs);
        }

        /// <summary>
        /// Gets a specific job by its ID.
        /// </summary>
        /// <param name="jobId">The ID of the job to retrieve.</param>
        /// <returns>The job status if found; otherwise, a 404 response.</returns>
        [HttpGet("{jobId}")]
        [ProducesResponseType(typeof(JobStatus), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJob(string jobId)
        {
            var job = await _jobManager.GetJobStatusAsync(jobId);
            if (job == null || job.JobId != jobId)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Job with ID {jobId} not found"
                });
            }

            return Ok(job);
        }

        /// <summary>
        /// Creates a new job with the specified configuration.
        /// </summary>
        /// <param name="request">The job creation request containing configuration details.</param>
        /// <returns>The created job information if successful; otherwise, an error response.</returns>
        [HttpPost]
        [ProducesResponseType(typeof(JobCreatedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
        {
            try
            {
                // Check if any job is already running
                if (_jobManager.IsAnyJobRunning())
                {
                    return Conflict(new ApiResponse<object>
                    {
                        Success = false,
                        Message = JOB_ALREADY_RUNNING_ERROR,
                        Data = new { ActiveJobId = _jobManager.GetActiveJobId() }
                    });
                }

                if (string.IsNullOrEmpty(request.StrategyType))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Strategy type is required"
                    });
                }

                // Check if the strategy type exists
                var strategies = _jobManager.GetAvailableStrategies();
                if (!strategies.ContainsKey(request.StrategyType))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Strategy type '{request.StrategyType}' not found"
                    });
                }

                // Create a new configuration
                var config = new JobConfiguration
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = request.Name,
                    ConfigurationId = request.ConfigurationId,
                    StrategyType = request.StrategyType,
                    ReaderSettingsGroup = request.ReaderSettings,
                    Parameters = request.Parameters
                };

                // Generate a log file path
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string safeJobName = new string(config.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());
                config.LogFilePath = Path.Combine("Logs", $"{safeJobName}_{timestamp}.csv");

                // Save the configuration
                var savedConfig = await _configService.CreateConfigurationAsync(config);

                // Register the job
                var jobId = await _jobManager.RegisterJobAsync(savedConfig);

                var response = new JobCreatedResponse
                {
                    JobId = jobId,
                    Name = savedConfig.Name,
                    LogFilePath = savedConfig.LogFilePath
                };

                return CreatedAtAction(nameof(GetJob), new { jobId }, response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating job");
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error creating job: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Starts a specific job.
        /// </summary>
        /// <param name="jobId">The ID of the job to start.</param>
        /// <param name="request">Optional start parameters including timeout.</param>
        /// <returns>The updated job status if successful; otherwise, an error response.</returns>
        [HttpPost("{jobId}/start")]
        [ProducesResponseType(typeof(ApiResponse<JobStatus>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> StartJob(string jobId, [FromBody] object requestObj)
        {
            try
            {
                // Check if another job is already running (that's not this job)
                string activeJobId = _jobManager.GetActiveJobId();
                if (!string.IsNullOrEmpty(activeJobId) && activeJobId != jobId)
                {
                    return Conflict(new ApiResponse<object>
                    {
                        Success = false,
                        Message = JOB_ALREADY_RUNNING_ERROR,
                        Data = new { ActiveJobId = activeJobId }
                    });
                }

                // Check if the job exists
                var job = await _jobManager.GetJobStatusAsync(jobId);
                if (job == null || job.JobId != jobId)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Job with ID {jobId} not found"
                    });
                }

                // Check if the job is already running
                if (job.State == JobState.Running)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Job with ID {jobId} is already running"
                    });
                }

                // Parse the request manually to avoid model validation
                int timeout = 300;
                if (requestObj != null)
                {
                    try
                    {
                        var jsonElement = (JsonElement)requestObj;
                        if (jsonElement.TryGetProperty("TimeoutSeconds", out var timeoutElement) && 
                            timeoutElement.TryGetInt32(out var timeoutValue))
                        {
                            timeout = timeoutValue;
                        }
                    }
                    catch
                    {
                        // If parsing fails, use default timeout
                    }
                }

                bool success = await _jobManager.StartJobAsync(jobId, timeout);

                if (!success)
                {
                    // If starting failed but it's not because of another running job
                    if (_jobManager.IsAnyJobRunning() && _jobManager.GetActiveJobId() != jobId)
                    {
                        return Conflict(new ApiResponse<object>
                        {
                            Success = false,
                            Message = JOB_ALREADY_RUNNING_ERROR,
                            Data = new { ActiveJobId = _jobManager.GetActiveJobId() }
                        });
                    }
                    else
                    {
                        return BadRequest(new ApiResponse<object>
                        {
                            Success = false,
                            Message = $"Failed to start job with ID {jobId}"
                        });
                    }
                }

                // Get the updated status
                var updatedJob = await _jobManager.GetJobStatusAsync(jobId);

                return Ok(new ApiResponse<JobStatus>
                {
                    Success = true,
                    Message = $"Job with ID {jobId} started successfully",
                    Data = updatedJob
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting job {JobId}", jobId);
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error starting job: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Stops a running job.
        /// </summary>
        /// <param name="jobId">The ID of the job to stop.</param>
        /// <returns>The updated job status if successful; otherwise, an error response.</returns>
        [HttpPost("{jobId}/stop")]
        [ProducesResponseType(typeof(ApiResponse<JobStatus>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StopJob(string jobId)
        {
            try
            {
                // Check if the job exists
                var job = await _jobManager.GetJobStatusAsync(jobId);
                if (job == null || job.JobId != jobId)
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Job with ID {jobId} not found"
                    });
                }

                // Check if the job is running
                if (job.State != JobState.Running)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Job with ID {jobId} is not running"
                    });
                }

                // Stop the job
                bool success = await _jobManager.StopJobAsync(jobId);

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Failed to stop job with ID {jobId}"
                    });
                }

                // Get the updated status
                var updatedJob = await _jobManager.GetJobStatusAsync(jobId);

                return Ok(new ApiResponse<JobStatus>
                {
                    Success = true,
                    Message = $"Job with ID {jobId} stopped successfully",
                    Data = updatedJob
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping job {JobId}", jobId);
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error stopping job: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Gets the currently active job, if any.
        /// </summary>
        /// <returns>The active job status if there is one; otherwise, a 404 response.</returns>
        [HttpGet("active")]
        [ProducesResponseType(typeof(ApiResponse<JobStatus>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetActiveJob()
        {
            string activeJobId = _jobManager.GetActiveJobId();

            if (string.IsNullOrEmpty(activeJobId))
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = "No active job found"
                });
            }

            var job = await _jobManager.GetJobStatusAsync(activeJobId);

            if (job == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Active job not found in database"
                });
            }

            return Ok(new ApiResponse<JobStatus>
            {
                Success = true,
                Message = "Active job found",
                Data = job
            });
        }

        /// <summary>
        /// Gets metrics for a specific job.
        /// </summary>
        /// <param name="jobId">The ID of the job.</param>
        /// <returns>The job metrics if found; otherwise, a 404 response.</returns>
        [HttpGet("{jobId}/metrics")]
        [ProducesResponseType(typeof(JobMetricsResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJobMetrics(string jobId)
        {
            // Check if the job exists
            var job = await _jobManager.GetJobStatusAsync(jobId);
            if (job == null || job.JobId != jobId)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Job with ID {jobId} not found"
                });
            }

            var metrics = await _jobManager.GetJobMetricsAsync(jobId);

            return Ok(new JobMetricsResponse
            {
                JobId = jobId,
                Metrics = metrics
            });
        }

        /// <summary>
        /// Gets log entries for a specific job.
        /// </summary>
        /// <param name="jobId">The ID of the job.</param>
        /// <param name="maxEntries">Maximum number of log entries to return (default: 100).</param>
        /// <returns>The job logs if found; otherwise, a 404 response.</returns>
        [HttpGet("{jobId}/logs")]
        [ProducesResponseType(typeof(JobLogResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJobLogs(string jobId, [FromQuery] int maxEntries = 100)
        {
            // Check if the job exists
            var job = await _jobManager.GetJobStatusAsync(jobId);
            if (job == null || job.JobId != jobId)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Job with ID {jobId} not found"
                });
            }

            var logs = await _jobManager.GetJobLogEntriesAsync(jobId, maxEntries);

            return Ok(new JobLogResponse
            {
                JobId = jobId,
                LogEntries = logs
            });
        }

        /// <summary>
        /// Gets a list of available job strategies.
        /// </summary>
        /// <returns>A dictionary of strategy types and their descriptions.</returns>
        [HttpGet("strategies")]
        [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
        public IActionResult GetAvailableStrategies()
        {
            var strategies = _jobManager.GetAvailableStrategies();
            return Ok(strategies);
        }

        [HttpGet("{jobId}/tags")]
        [ProducesResponseType(typeof(ApiResponse<TagDataResponse>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetJobTags(
                string jobId,
                [FromQuery] int page = 1,
                [FromQuery] int pageSize = 50,
                [FromQuery] string sortBy = "timestamp",
                [FromQuery] bool descending = true,
                [FromQuery] string filter = null)
        {
            // Check if the job exists
            var job = _jobManager.GetJobStatusAsync(jobId).GetAwaiter().GetResult();
            if (job == null || job.JobId != jobId)
            {
                return NotFound(new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Job with ID {jobId} not found"
                });
            }

            var allTagData = _jobManager.GetJobTagData(jobId);

            // Apply filtering if specified
            var filteredTags = allTagData.Tags;
            if (!string.IsNullOrEmpty(filter))
            {
                filter = filter.ToLowerInvariant();
                filteredTags = filteredTags
                    .Where(t =>
                        t.TID.ToLowerInvariant().Contains(filter) ||
                        t.EPC.ToLowerInvariant().Contains(filter))
                    .ToList();
            }

            // Apply sorting
            filteredTags = sortBy.ToLowerInvariant() switch
            {
                "tid" => descending ? filteredTags.OrderByDescending(t => t.TID).ToList() : filteredTags.OrderBy(t => t.TID).ToList(),
                "epc" => descending ? filteredTags.OrderByDescending(t => t.EPC).ToList() : filteredTags.OrderBy(t => t.EPC).ToList(),
                "readcount" => descending ? filteredTags.OrderByDescending(t => t.ReadCount).ToList() : filteredTags.OrderBy(t => t.ReadCount).ToList(),
                "rssi" => descending ? filteredTags.OrderByDescending(t => t.RSSI).ToList() : filteredTags.OrderBy(t => t.RSSI).ToList(),
                "antenna" => descending ? filteredTags.OrderByDescending(t => t.AntennaPort).ToList() : filteredTags.OrderBy(t => t.AntennaPort).ToList(),
                _ => descending ? filteredTags.OrderByDescending(t => t.Timestamp).ToList() : filteredTags.OrderBy(t => t.Timestamp).ToList()
            };

            // Apply paging
            int totalItems = filteredTags.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            // Ensure page is within bounds
            page = Math.Max(1, Math.Min(page, totalPages == 0 ? 1 : totalPages));

            var pagedTags = filteredTags
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            // Create response
            var response = new TagDataResponse
            {
                JobId = jobId,
                Tags = pagedTags,
                TotalCount = allTagData.TotalCount,
                UniqueCount = allTagData.UniqueCount,
                LastUpdated = allTagData.LastUpdated
            };

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = $"Found {totalItems} matching tags (page {page} of {totalPages})",
                Data = new
                {
                    Tags = pagedTags,
                    Pagination = new
                    {
                        Page = page,
                        PageSize = pageSize,
                        TotalItems = totalItems,
                        TotalPages = totalPages
                    },
                    Summary = new
                    {
                        TotalReads = allTagData.TotalCount,
                        UniqueTagCount = allTagData.UniqueCount,
                        LastUpdated = allTagData.LastUpdated
                    }
                }
            });
        }
    }
}