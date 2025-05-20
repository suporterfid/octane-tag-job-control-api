using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Impinj.OctaneSdk;

namespace OctaneTagWritingTest.Helpers
{
    /// <summary>
    /// Extension to TagOpController that adds metrics and analytics capabilities
    /// </summary>
    public partial class TagOpController
    {
        // For tracking metrics
        private readonly ConcurrentDictionary<string, long> _writeTimesMs = new ConcurrentDictionary<string, long>();
        private readonly ConcurrentDictionary<string, long> _verifyTimesMs = new ConcurrentDictionary<string, long>();
        private readonly Stopwatch _readRateTimer = new Stopwatch();
        private long _totalReads = 0;
        private long _lastReadCount = 0;
        private readonly object _readRateLock = new object();
        private double _currentReadRate = 0;

        // EAN pattern for extracting from EPC codes
        private static readonly Regex _eanPattern = new Regex(@"^[0-9]{13}$", RegexOptions.Compiled);

        /// <summary>
        /// Extracts the European Article Number (EAN) from an EPC if available
        /// </summary>
        /// <param name="epc">The EPC to extract an EAN from</param>
        /// <returns>The EAN if found, otherwise null</returns>
        public string GetEanFromEpc(string epc)
        {
            if (string.IsNullOrEmpty(epc))
                return null;

            try
            {
                // SGTIN-96 format: Header (8 bits) + Filter (3 bits) + Partition (3 bits) + 
                // Company Prefix + Item Reference + Serial

                // For SGTIN-96 with company prefix + item reference (roughly corresponds to EAN)
                if (epc.Length >= 24 && epc.StartsWith("30"))
                {
                    // Extract the company prefix and item reference portions
                    // This is a simplification - actual implementation would need to account for partition value
                    string eanCandidate = epc.Substring(4, 13);

                    // Verify it looks like an EAN
                    if (_eanPattern.IsMatch(eanCandidate))
                    {
                        return eanCandidate;
                    }
                }

                // GS1 EPC format - look for EAN-13 embedded within the EPC
                if (epc.Length >= 16)
                {
                    for (int i = 0; i <= epc.Length - 13; i++)
                    {
                        string subset = epc.Substring(i, 13);
                        if (_eanPattern.IsMatch(subset))
                        {
                            return subset;
                        }
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the count of tags that have been locked or permalocked
        /// </summary>
        /// <returns>The number of locked tags</returns>
        public int GetLockedTagsCount()
        {
            return _tagLockStatusCache.Count(kvp =>
                kvp.Value.AccessPasswordLockState != LockState.Unlocked ||
                kvp.Value.EpcLockState != LockState.Unlocked ||
                kvp.Value.KillPasswordLockState != LockState.Unlocked ||
                kvp.Value.UserMemoryLockState != LockState.Unlocked);
        }

        /// <summary>
        /// Gets the current read rate in tags per second
        /// </summary>
        /// <returns>Current read rate</returns>
        public double GetReadRate()
        {
            lock (_readRateLock)
            {
                // Initialize timer if not started
                if (!_readRateTimer.IsRunning)
                {
                    _readRateTimer.Start();
                    _lastReadCount = _totalReads;
                    return 0;
                }

                // Calculate read rate every second
                if (_readRateTimer.ElapsedMilliseconds >= 1000)
                {
                    long currentCount = _totalReads;
                    long difference = currentCount - _lastReadCount;
                    double seconds = _readRateTimer.ElapsedMilliseconds / 1000.0;

                    _currentReadRate = difference / seconds;
                    _lastReadCount = currentCount;
                    _readRateTimer.Restart();
                }

                return _currentReadRate;
            }
        }

        /// <summary>
        /// Records a tag read for read rate calculation
        /// </summary>
        public void RecordTagRead()
        {
            lock (_readRateLock)
            {
                _totalReads++;
            }
        }

        /// <summary>
        /// Records the write time for a tag operation
        /// </summary>
        /// <param name="tid">Tag ID</param>
        /// <param name="timeMs">Time in milliseconds</param>
        public void RecordWriteTime(string tid, long timeMs)
        {
            if (!string.IsNullOrEmpty(tid) && timeMs > 0)
            {
                _writeTimesMs[tid] = timeMs;
            }
        }

        /// <summary>
        /// Records the verification time for a tag operation
        /// </summary>
        /// <param name="tid">Tag ID</param>
        /// <param name="timeMs">Time in milliseconds</param>
        public void RecordVerifyTime(string tid, long timeMs)
        {
            if (!string.IsNullOrEmpty(tid) && timeMs > 0)
            {
                _verifyTimesMs[tid] = timeMs;
            }
        }

        /// <summary>
        /// Gets the average write time in milliseconds
        /// </summary>
        /// <returns>Average write time or 0 if no data</returns>
        public double GetAvgWriteTimeMs()
        {
            if (_writeTimesMs.Count == 0)
                return 0;

            return _writeTimesMs.Values.Average();
        }

        /// <summary>
        /// Gets the average verification time in milliseconds
        /// </summary>
        /// <returns>Average verification time or 0 if no data</returns>
        public double GetAvgVerifyTimeMs()
        {
            if (_verifyTimesMs.Count == 0)
                return 0;

            return _verifyTimesMs.Values.Average();
        }

        /// <summary>
        /// Gets the count of failed tag operations
        /// </summary>
        /// <returns>Number of failed operations</returns>
        public int GetFailureCount()
        {
            lock (lockObj)
            {
                // Calculate failed operations as total operations minus successful ones
                return operationResultByTid.Count - operationResultWithSuccessByTid.Count;
            }
        }

        /// <summary>
        /// Overrides the base TriggerWriteAndVerify method to also record timing metrics
        /// </summary>
        public void TriggerWriteAndVerifyWithMetrics(Tag tag, string newEpcToWrite, ImpinjReader reader,
            CancellationToken cancellationToken, Stopwatch swWrite, string newAccessPassword,
            bool encodeOrDefault, ushort targetAntennaPort = 1)
        {
            // Call the original method
            TriggerWriteAndVerify(tag, newEpcToWrite, reader, cancellationToken,
                swWrite, newAccessPassword, encodeOrDefault, targetAntennaPort);

            // Record tag read for read rate calculation
            RecordTagRead();
        }


    }
}
