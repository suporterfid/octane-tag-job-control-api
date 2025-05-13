using Microsoft.AspNetCore.Mvc;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services;

namespace OctaneTagJobControlAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class JobController : ControllerBase
    {
        private readonly ILogger<JobController> _logger;
        private readonly JobManager _jobManager;
        private readonly JobConfigurationService _configService;

        public JobController(
            ILogger<JobController> logger,
            JobManager jobManager,
            JobConfigurationService configService)
        {
            _logger = logger;
            _jobManager = jobManager;
            _configService = configService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<JobStatus>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllJobs()
        {
            var jobs = await _jobManager.GetAllJobStatusesAsync();
            return Ok(jobs);
        }

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

        [HttpPost]
        [ProducesResponseType(typeof(JobCreatedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateJob([FromBody] CreateJobRequest request)
        {
            try
            {
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
                    StrategyType = request.StrategyType,
                    ReaderSettings = request.ReaderSettings,
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

        [HttpPost("{jobId}/start")]
        [ProducesResponseType(typeof(ApiResponse<JobStatus>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> StartJob(string jobId, [FromBody] StartJobRequest request)
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

                // Check if the job is already running
                if (job.State == JobState.Running)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Job with ID {jobId} is already running"
                    });
                }

                // Start the job
                int timeout = request != null ? request.TimeoutSeconds : 300;
                bool success = await _jobManager.StartJobAsync(jobId, timeout);

                if (!success)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Failed to start job with ID {jobId}"
                    });
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

        [HttpGet("strategies")]
        [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
        public IActionResult GetAvailableStrategies()
        {
            var strategies = _jobManager.GetAvailableStrategies();
            return Ok(strategies);
        }
    }
}