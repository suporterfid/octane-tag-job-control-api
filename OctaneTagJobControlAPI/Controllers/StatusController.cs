using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using OctaneTagJobControlAPI.Models;
using OctaneTagJobControlAPI.Services;
using Microsoft.AspNetCore.Mvc;
using OctaneTagWritingTest.Helpers;
using System.IO;

namespace OctaneTagJobControlAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StatusController : ControllerBase
    {
        private readonly ILogger<StatusController> _logger;
        private readonly JobManager _jobManager;
        private readonly IWebHostEnvironment _environment;
        private static readonly DateTime _startTime = DateTime.UtcNow;

        public StatusController(
            ILogger<StatusController> logger,
            JobManager jobManager,
            IWebHostEnvironment environment)
        {
            _logger = logger;
            _jobManager = jobManager;
            _environment = environment;
        }

        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<Dictionary<string, object>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetStatus()
        {
            var process = Process.GetCurrentProcess();
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "Unknown";
            var uptime = (DateTime.UtcNow - _startTime).ToString(@"dd\.hh\:mm\:ss");

            // Get job statistics
            var jobs = await _jobManager.GetAllJobStatusesAsync();
            var runningJobs = jobs.Count(j => j.State == JobState.Running);
            var completedJobs = jobs.Count(j => j.State == JobState.Completed);
            var failedJobs = jobs.Count(j => j.State == JobState.Failed);

            // Get tag statistics
            var totalTags = TagOpController.Instance.GetTotalReadCount();
            var successTags = TagOpController.Instance.GetSuccessCount();

            // Get system memory usage
            var memoryUsageMB = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2);

            // Get disk space info
            var contentRootPath = _environment.ContentRootPath;
            var driveInfo = new DriveInfo(Path.GetPathRoot(contentRootPath));
            var totalSpaceGB = Math.Round(driveInfo.TotalSize / 1024.0 / 1024.0 / 1024.0, 2);
            var freeSpaceGB = Math.Round(driveInfo.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0, 2);
            var freeSpacePercentage = Math.Round((double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize * 100, 2);

            var statusInfo = new Dictionary<string, object>
            {
                { "apiName", "RFID Job Control API" },
                { "version", version },
                { "environment", _environment.EnvironmentName },
                { "uptime", uptime },
                { "startTime", _startTime.ToString("yyyy-MM-dd HH:mm:ss UTC") },
                { "currentTime", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC") },
                
                // System info
                { "machineName", Environment.MachineName },
                { "osVersion", RuntimeInformation.OSDescription },
                { "frameworkVersion", RuntimeInformation.FrameworkDescription },
                { "processorArchitecture", RuntimeInformation.ProcessArchitecture.ToString() },
                { "processorCount", Environment.ProcessorCount },
                
                // Resource usage
                { "memoryUsageMB", memoryUsageMB },
                { "threadCount", process.Threads.Count },
                { "handleCount", process.HandleCount },
                { "totalDiskSpaceGB", totalSpaceGB },
                { "freeDiskSpaceGB", freeSpaceGB },
                { "freeDiskSpacePercentage", freeSpacePercentage },
                
                // Job statistics
                { "jobsTotal", jobs.Count },
                { "jobsRunning", runningJobs },
                { "jobsCompleted", completedJobs },
                { "jobsFailed", failedJobs },
                
                // Tag statistics
                { "tagsTotal", totalTags },
                { "tagsSuccess", successTags },
                { "tagsFailure", totalTags - successTags },
                { "successRate", totalTags > 0
                    ? Math.Round((double)successTags / totalTags * 100, 2)
                    : 0 }
            };

            return Ok(new ApiResponse<Dictionary<string, object>>
            {
                Success = true,
                Message = "System status",
                Data = statusInfo
            });
        }

        [HttpGet("version")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public IActionResult GetVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version?.ToString() ?? "Unknown";
            var fileVersion = FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion ?? "Unknown";
            var productVersion = FileVersionInfo.GetVersionInfo(assembly.Location).ProductVersion ?? "Unknown";

            var versionInfo = new
            {
                AssemblyVersion = version,
                FileVersion = fileVersion,
                ProductVersion = productVersion,
                BuildDate = GetBuildDate(assembly)
            };

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "API Version Information",
                Data = versionInfo
            });
        }

        [HttpGet("readers")]
        [ProducesResponseType(typeof(ApiResponse<List<object>>), StatusCodes.Status200OK)]
        public IActionResult GetConnectedReaders()
        {
            var readers = new List<object>
            {
                new
                {
                    Role = "Detector",
                    Hostname = "192.168.68.248",
                    Status = "Connected",
                    LastSeen = DateTime.UtcNow.AddMinutes(-2).ToString("yyyy-MM-dd HH:mm:ss UTC")
                },
                new
                {
                    Role = "Writer",
                    Hostname = "192.168.1.100",
                    Status = "Connected",
                    LastSeen = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                },
                new
                {
                    Role = "Verifier",
                    Hostname = "192.168.68.93",
                    Status = "Connected",
                    LastSeen = DateTime.UtcNow.AddSeconds(-30).ToString("yyyy-MM-dd HH:mm:ss UTC")
                }
            };

            return Ok(new ApiResponse<List<object>>
            {
                Success = true,
                Message = "Connected Readers",
                Data = readers
            });
        }

        [HttpGet("metrics")]
        [ProducesResponseType(typeof(ApiResponse<Dictionary<string, object>>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMetrics()
        {
            var process = Process.GetCurrentProcess();

            // Calculate system metrics
            var cpuTimeTotal = process.TotalProcessorTime.TotalMilliseconds;
            var uptime = (DateTime.UtcNow - _startTime).TotalMilliseconds;
            var cpuUsage = Math.Round(cpuTimeTotal / (Environment.ProcessorCount * uptime) * 100, 2);

            // Get tag operation metrics
            var totalTagsProcessed = TagOpController.Instance.GetTotalReadCount();
            var successfulTags = TagOpController.Instance.GetSuccessCount();
            var failureRate = totalTagsProcessed > 0
                ? Math.Round(100.0 - (successfulTags * 100.0 / totalTagsProcessed), 2)
                : 0;

            // Get job statistics
            var jobs = await _jobManager.GetAllJobStatusesAsync();
            var runningJobs = jobs.Count(j => j.State == JobState.Running);

            // Get current memory usage
            var currentMemoryBytes = process.WorkingSet64;
            var peakMemoryBytes = process.PeakWorkingSet64;
            var privateMemoryBytes = process.PrivateMemorySize64;

            // Calculate throughput
            var startTimeSpan = DateTime.UtcNow - _startTime;
            var tagsPerSecond = startTimeSpan.TotalSeconds > 0
                ? Math.Round(totalTagsProcessed / startTimeSpan.TotalSeconds, 2)
                : 0;
            var successPerSecond = startTimeSpan.TotalSeconds > 0
                ? Math.Round(successfulTags / startTimeSpan.TotalSeconds, 2)
                : 0;

            var metrics = new Dictionary<string, object>
            {
                // System metrics
                { "cpuUsagePercent", cpuUsage },
                { "memoryUsageMB", Math.Round(currentMemoryBytes / 1024.0 / 1024.0, 2) },
                { "peakMemoryUsageMB", Math.Round(peakMemoryBytes / 1024.0 / 1024.0, 2) },
                { "privateMemoryMB", Math.Round(privateMemoryBytes / 1024.0 / 1024.0, 2) },
                { "threadCount", process.Threads.Count },
                { "handleCount", process.HandleCount },
                { "uptimeSeconds", Math.Round((DateTime.UtcNow - _startTime).TotalSeconds, 0) },
                
                // Job metrics
                { "totalJobs", jobs.Count },
                { "runningJobs", runningJobs },
                { "completedJobs", jobs.Count(j => j.State == JobState.Completed) },
                { "failedJobs", jobs.Count(j => j.State == JobState.Failed) },
                
                // Tag processing metrics
                { "totalTagsProcessed", totalTagsProcessed },
                { "successfulTags", successfulTags },
                { "failedTags", totalTagsProcessed - successfulTags },
                { "successRate", totalTagsProcessed > 0
                    ? Math.Round(successfulTags * 100.0 / totalTagsProcessed, 2)
                    : 0 },
                { "failureRate", failureRate },
                
                // Throughput metrics
                { "tagsPerSecond", tagsPerSecond },
                { "successPerSecond", successPerSecond },
                
                // Time metrics
                { "processCpuTimeSeconds", Math.Round(process.TotalProcessorTime.TotalSeconds, 2) },
                { "userCpuTimeSeconds", Math.Round(process.UserProcessorTime.TotalSeconds, 2) },
                { "privilegedCpuTimeSeconds", Math.Round(process.PrivilegedProcessorTime.TotalSeconds, 2) },
            };

            return Ok(new ApiResponse<Dictionary<string, object>>
            {
                Success = true,
                Message = "System Metrics",
                Data = metrics
            });
        }

        [HttpGet("health")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> GetHealth()
        {
            try
            {
                // Check system health conditions
                bool isDiskHealthy = CheckDiskHealth();
                bool isMemoryHealthy = CheckMemoryHealth();
                bool isJobSystemHealthy = await CheckJobSystemHealthAsync();

                var healthStatus = new
                {
                    Status = isDiskHealthy && isMemoryHealthy && isJobSystemHealthy ? "Healthy" : "Unhealthy",
                    Components = new
                    {
                        Disk = new { Status = isDiskHealthy ? "Healthy" : "Unhealthy" },
                        Memory = new { Status = isMemoryHealthy ? "Healthy" : "Unhealthy" },
                        JobSystem = new { Status = isJobSystemHealthy ? "Healthy" : "Unhealthy" }
                    },
                    Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                };

                if (isDiskHealthy && isMemoryHealthy && isJobSystemHealthy)
                {
                    return Ok(new ApiResponse<object>
                    {
                        Success = true,
                        Message = "System is healthy",
                        Data = healthStatus
                    });
                }
                else
                {
                    return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiResponse<object>
                    {
                        Success = false,
                        Message = "System is not healthy",
                        Data = healthStatus
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking system health");
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiResponse<object>
                {
                    Success = false,
                    Message = "Error checking system health",
                    Data = new { Status = "Error", Error = ex.Message }
                });
            }
        }

        [HttpGet("logs")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public IActionResult GetSystemLogs([FromQuery] int maxEntries = 100)
        {
            // This is a simplified implementation - in a real system you would
            // read from actual log files or a logging database

            var logs = new List<object>();
            var logDirectory = Path.Combine(_environment.ContentRootPath, "Logs");

            if (Directory.Exists(logDirectory))
            {
                // Get most recent log files
                var logFiles = Directory.GetFiles(logDirectory, "*.log")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Take(5)
                    .ToList();

                foreach (var logFile in logFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(logFile);
                        var fileName = Path.GetFileName(logFile);
                        var lastLines = ReadLastLines(logFile, Math.Min(maxEntries, 20));

                        logs.Add(new
                        {
                            LogFile = fileName,
                            LastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            SizeKB = Math.Round(fileInfo.Length / 1024.0, 2),
                            RecentEntries = lastLines
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error reading log file {LogFile}", logFile);
                    }
                }
            }

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "System Logs",
                Data = new
                {
                    LogsDirectory = logDirectory,
                    LogFiles = logs
                }
            });
        }

        [HttpGet("files")]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
        public IActionResult GetSystemFiles([FromQuery] string path = "")
        {
            try
            {
                // Normalize and secure the path to prevent directory traversal
                var basePath = _environment.ContentRootPath;
                var requestedPath = Path.GetFullPath(Path.Combine(basePath, path));

                // Ensure the path is within the application directory
                if (!requestedPath.StartsWith(basePath))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Invalid path. Path must be within the application directory."
                    });
                }

                // Check if path exists
                if (!Directory.Exists(requestedPath))
                {
                    return NotFound(new ApiResponse<object>
                    {
                        Success = false,
                        Message = $"Directory not found: {path}"
                    });
                }

                // Get directories
                var directories = Directory.GetDirectories(requestedPath)
                    .Select(d => new
                    {
                        Name = Path.GetFileName(d),
                        Type = "Directory",
                        Path = Path.GetRelativePath(basePath, d).Replace('\\', '/'),
                        LastModified = Directory.GetLastWriteTime(d).ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToList();

                // Get files (fixed to avoid File method conflict)
                var filesList = Directory.GetFiles(requestedPath)
                    .Select(filePath =>
                    {
                        var fileInfo = new FileInfo(filePath);
                        return new
                        {
                            Name = Path.GetFileName(filePath),
                            Type = "File",
                            Path = Path.GetRelativePath(basePath, filePath).Replace('\\', '/'),
                            SizeBytes = fileInfo.Length,
                            SizeFormatted = FormatFileSize(fileInfo.Length),
                            LastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
                        };
                    })
                    .ToList();

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = $"Files and directories in {path}",
                    Data = new
                    {
                        CurrentPath = path,
                        ParentPath = GetParentPath(path),
                        Directories = directories,
                        Files = filesList  // Use the renamed variable here
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting files for path {Path}", path);
                return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error getting files: {ex.Message}"
                });
            }
        }

        #region Helper Methods

        private DateTime GetBuildDate(Assembly assembly)
        {
            const string BuildVersionMetadataPrefix = "+build";

            var attribute = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            if (attribute?.InformationalVersion != null)
            {
                var value = attribute.InformationalVersion;
                var index = value.IndexOf(BuildVersionMetadataPrefix);
                if (index > 0)
                {
                    value = value[(index + BuildVersionMetadataPrefix.Length)..];
                    if (DateTime.TryParseExact(value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                    {
                        return result;
                    }
                }
            }

            // Use fully qualified type name to avoid conflict with Controller.File method
            return System.IO.File.GetLastWriteTime(assembly.Location);
        }

        private bool CheckDiskHealth()
        {
            var contentRootPath = _environment.ContentRootPath;
            var driveInfo = new DriveInfo(Path.GetPathRoot(contentRootPath));

            // Consider disk healthy if at least 10% or 1GB of space is available
            var minFreeSpacePercentage = 10;
            var minFreeSpaceBytes = 1024L * 1024L * 1024L; // 1GB

            var freeSpacePercentage = (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize * 100;

            return freeSpacePercentage >= minFreeSpacePercentage ||
                   driveInfo.AvailableFreeSpace >= minFreeSpaceBytes;
        }

        private bool CheckMemoryHealth()
        {
            var process = Process.GetCurrentProcess();
            var memoryUsageMB = process.WorkingSet64 / 1024.0 / 1024.0;

            // Consider memory healthy if usage is below 80% of available system memory
            // Note: This is a simplified check, real implementations would use more sophisticated metrics
            var maxHealthyMemoryMB = Environment.SystemPageSize * (double)Environment.ProcessorCount * 10;

            return memoryUsageMB < maxHealthyMemoryMB;
        }

        private async Task<bool> CheckJobSystemHealthAsync()
        {
            var jobs = await _jobManager.GetAllJobStatusesAsync();

            // Check for any jobs that have been running for too long (>24h)
            var longRunningJobs = jobs.Where(j =>
                j.State == JobState.Running &&
                j.StartTime.HasValue &&
                (DateTime.UtcNow - j.StartTime.Value).TotalHours > 24).Count();

            // Consider job system healthy if there are no excessively long-running jobs
            // and no more than 50% of jobs are in failed state
            var failedJobPercentage = jobs.Count > 0
                ? (double)jobs.Count(j => j.State == JobState.Failed) / jobs.Count * 100
                : 0;

            return longRunningJobs == 0 && failedJobPercentage < 50;
        }

        private static string[] ReadLastLines(string filePath, int lineCount)
        {
            var result = new List<string>();
            var buffer = new LinkedList<string>();

            using (var reader = new StreamReader(filePath))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    buffer.AddLast(line);
                    if (buffer.Count > lineCount)
                    {
                        buffer.RemoveFirst();
                    }
                }
            }

            return buffer.ToArray();
        }

        private static string GetParentPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var parts = path.Split('/', '\\').Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (parts.Count <= 1)
                return string.Empty;

            parts.RemoveAt(parts.Count - 1);
            return string.Join('/', parts);
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;

            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }

            return $"{number:n1} {suffixes[counter]}";
        }

        #endregion
    }
}