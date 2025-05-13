using System.IO;

namespace Impinj.TagUtils
{
    public class TagRelatedSettings : ExampleSettings
    {
        public bool QueryWhichTagsToUse { get; set; } = true;

        public bool HitEnterToExecuteOperations { get; set; } = false;

        public int TagReadingInMilliseconds { get; set; } = 5000;

        public int EmptyFieldTimeoutInMilliseconds { get; set; } = 10000;

        public int TagAccessTimeoutInMilliseconds { get; set; } = 5000;

        public int TagAccessRetryCount { get; set; } = 0;

        public bool UseFastId { get; set; } = true;

        public string DefaultAccessPassword { get; set; } = string.Empty;

        public string AutomatedCommandsFile { get; set; } = Path.Combine("Data", "commands.txt");
    }
}
