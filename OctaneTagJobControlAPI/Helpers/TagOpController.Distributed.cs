using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Impinj.OctaneSdk;

namespace OctaneTagWritingTest.Helpers
{
    /// <summary>
    /// Extension method for TagOpController to support distributed RFID operations
    /// </summary>
    public partial class TagOpController
    {
        // Dictionary to track tags that were seen by a detector/reader but need processing by a writer
        private readonly ConcurrentDictionary<string, TagSeenInfo> _seenTagsInfo = new();

        /// <summary>
        /// Structure to hold information about detected tags for distributed processing
        /// </summary>
        public class TagSeenInfo
        {
            public string TidHex { get; set; }
            public string CurrentEpc { get; set; }
            public string ExpectedEpc { get; set; }
            public string ChipModel { get; set; }
            public DateTime DetectedTime { get; set; }
            public bool ProcessedByWriter { get; set; }
            public bool ProcessedByVerifier { get; set; }
        }

        /// <summary>
        /// Records a tag as seen by a detector/reader without immediately writing to it
        /// Used in distributed processing scenarios where detection and writing happen in separate instances
        /// </summary>
        /// <param name="tidHex">The tag's TID in hexadecimal</param>
        /// <param name="currentEpc">The tag's current EPC</param>
        /// <param name="expectedEpc">The expected EPC for the tag</param>
        /// <param name="chipModel">The chip model of the tag</param>
        /// <returns>True if this is the first time seeing this tag, false if it was already seen</returns>
        public bool RecordTagSeen(string tidHex, string currentEpc, string expectedEpc, string chipModel)
        {
            if (string.IsNullOrEmpty(tidHex))
                return false;

            return _seenTagsInfo.TryAdd(tidHex, new TagSeenInfo
            {
                TidHex = tidHex,
                CurrentEpc = currentEpc,
                ExpectedEpc = expectedEpc,
                ChipModel = chipModel,
                DetectedTime = DateTime.UtcNow,
                ProcessedByWriter = false,
                ProcessedByVerifier = false
            });
        }

        /// <summary>
        /// Checks if a tag has been previously seen by any reader but not yet processed by a writer
        /// Useful for writer instances to identify tags that need processing
        /// </summary>
        /// <param name="tidHex">The tag's TID in hexadecimal</param>
        /// <returns>True if the tag has been seen but not processed by a writer</returns>
        public bool HasUnprocessedTag(string tidHex)
        {
            if (string.IsNullOrEmpty(tidHex) || !_seenTagsInfo.TryGetValue(tidHex, out var info))
                return false;

            return !info.ProcessedByWriter;
        }

        /// <summary>
        /// Gets information about a tag that was previously seen
        /// </summary>
        /// <param name="tidHex">The tag's TID in hexadecimal</param>
        /// <returns>The tag information if found, otherwise null</returns>
        public TagSeenInfo GetTagSeenInfo(string tidHex)
        {
            if (string.IsNullOrEmpty(tidHex) || !_seenTagsInfo.TryGetValue(tidHex, out var info))
                return null;

            return info;
        }

        /// <summary>
        /// Marks a tag as processed by a writer
        /// </summary>
        /// <param name="tidHex">The tag's TID in hexadecimal</param>
        /// <returns>True if the tag was found and marked as processed</returns>
        public bool MarkTagProcessedByWriter(string tidHex)
        {
            if (string.IsNullOrEmpty(tidHex) || !_seenTagsInfo.TryGetValue(tidHex, out var info))
                return false;

            info.ProcessedByWriter = true;
            return true;
        }

        /// <summary>
        /// Marks a tag as processed by a verifier
        /// </summary>
        /// <param name="tidHex">The tag's TID in hexadecimal</param>
        /// <returns>True if the tag was found and marked as processed</returns>
        public bool MarkTagProcessedByVerifier(string tidHex)
        {
            if (string.IsNullOrEmpty(tidHex) || !_seenTagsInfo.TryGetValue(tidHex, out var info))
                return false;

            info.ProcessedByVerifier = true;
            return true;
        }

        /// <summary>
        /// Gets a list of all tags that have been seen but not yet processed by a writer
        /// </summary>
        /// <returns>A list of unprocessed tag TIDs</returns>
        public List<string> GetUnprocessedTagTids()
        {
            var result = new List<string>();
            foreach (var kvp in _seenTagsInfo)
            {
                if (!kvp.Value.ProcessedByWriter)
                {
                    result.Add(kvp.Key);
                }
            }
            return result;
        }

        /// <summary>
        /// Gets a list of all tags that have been processed by a writer but not verified
        /// </summary>
        /// <returns>A list of unverified tag TIDs</returns>
        public List<string> GetUnverifiedTagTids()
        {
            var result = new List<string>();
            foreach (var kvp in _seenTagsInfo)
            {
                if (kvp.Value.ProcessedByWriter && !kvp.Value.ProcessedByVerifier)
                {
                    result.Add(kvp.Key);
                }
            }
            return result;
        }

        /// <summary>
        /// Cleans up old tag entries from the seen tags dictionary
        /// </summary>
        /// <param name="maxAgeMinutes">Maximum age of entries to keep in minutes</param>
        public void CleanupOldTagEntries(int maxAgeMinutes = 60)
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-maxAgeMinutes);
            
            foreach (var kvp in _seenTagsInfo)
            {
                if (kvp.Value.DetectedTime < cutoffTime)
                {
                    _seenTagsInfo.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}