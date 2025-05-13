using EpcListGenerator;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OctaneTagJobControlAPI.Services
{
    /// <summary>
    /// Provides utility methods for the application
    /// </summary>
    public static class UtilityService
    {
        /// <summary>
        /// Generate a unique EPC for a tag using the given header and SKU
        /// </summary>
        public static string GenerateEpcForTid(string epcHeader, string sku, string tid)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(epcHeader) || epcHeader.Length != 2)
            {
                throw new ArgumentException("EPC header must be exactly 2 characters", nameof(epcHeader));
            }

            if (string.IsNullOrEmpty(sku) || sku.Length != 12)
            {
                throw new ArgumentException("SKU must be exactly 12 characters", nameof(sku));
            }

            if (string.IsNullOrEmpty(tid))
            {
                throw new ArgumentException("TID cannot be empty", nameof(tid));
            }

            try
            {
                // Use EpcListGeneratorHelper to generate the EPC
                var epc = EpcListGeneratorHelper.Instance.GenerateEpcFromTid(tid, epcHeader, epcHeader);
                return epc;
            }
            catch (Exception ex)
            {
                // If the generator fails, use a simpler approach
                string safeTid = tid.Length >= 10 ? tid.Substring(tid.Length - 10) : tid.PadLeft(10, '0');
                return epcHeader + epcHeader + sku + safeTid;
            }
        }

        /// <summary>
        /// Get detailed system information
        /// </summary>
        public static Dictionary<string, object> GetSystemInfo()
        {
            var info = new Dictionary<string, object>();

            var process = Process.GetCurrentProcess();

            info["processName"] = process.ProcessName;
            info["processId"] = process.Id;
            info["startTime"] = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            info["memoryUsageMB"] = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2);
            info["cpuTime"] = process.TotalProcessorTime.TotalSeconds;
            info["threadCount"] = process.Threads.Count;

            info["osDescription"] = RuntimeInformation.OSDescription;
            info["frameworkDescription"] = RuntimeInformation.FrameworkDescription;
            info["processorArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString();
            info["processorCount"] = Environment.ProcessorCount;

            info["machineName"] = Environment.MachineName;
            info["userDomainName"] = Environment.UserDomainName;
            info["userName"] = Environment.UserName;

            info["currentDirectory"] = Environment.CurrentDirectory;

            return info;
        }

        /// <summary>
        /// Generate a log file path
        /// </summary>
        public static string GenerateLogFilePath(string jobName, string extension = "csv")
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeJobName = string.IsNullOrEmpty(jobName)
                ? "Job"
                : new string(jobName.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray());

            return Path.Combine("Logs", $"{safeJobName}_{timestamp}.{extension}");
        }

        /// <summary>
        /// Ensure all required directories exist
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            var directories = new[]
            {
                "Logs",
                "Configs",
                "Data"
            };

            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }
    }
}
