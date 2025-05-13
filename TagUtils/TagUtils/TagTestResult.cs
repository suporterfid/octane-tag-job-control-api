using CsvHelper.Configuration.Attributes;

namespace Impinj.TagUtils
{
    public class TagTestResult
    {
        public string Sample { get; set; } = "S";

        public string Chip { get; set; } = string.Empty;

        public string TID { get; set; } = string.Empty;

        public string EPC { get; set; } = string.Empty;

        [Name("PC Word")]
        public string PCWord { get; set; } = string.Empty;

        [Name("Access Pwd")]
        public string AccessPassword { get; set; } = string.Empty;

        [Name("Kill Pwd")]
        public string KillPassword { get; set; } = string.Empty;

        [Name("User memory")]
        public string UserMemory { get; set; } = string.Empty;

        [Name("Reserved memory range")]
        public string ReservedMemoryRange { get; set; } = string.Empty;

        [Name("Reserved memory data")]
        public string ReservedMemoryData { get; set; } = string.Empty;

        [Name("Access Pwd Lock Status")]
        public ETestLockStatus AccessPasswordLockStatus { get; set; } = ETestLockStatus.Unknown;

        [Name("Kill Pwd Lock Status")]
        public ETestLockStatus KillPasswordLockStatus { get; set; } = ETestLockStatus.Unknown;

        [Name("EPC Lock Status")]
        public ETestLockStatus EPCLockStatus { get; set; } = ETestLockStatus.Unknown;

        [Name("User Memory Lock Status")]
        public ETestLockStatus UserMemoryLockStatus { get; set; } = ETestLockStatus.Unknown;

        [Name("AutoTune Value")]
        public string AutoTuneValue { get; set; } = string.Empty;

        [Name("Pre-Serialization Verification Check")]
        public ETestResult PreSerializationVerificationCheck { get; set; } = ETestResult.None;

        [Name("Integra: TID Parity Check")]
        public ETestResult IntegraTIDParityCheck { get; set; } = ETestResult.None;

        [Name("Integra: MarginRead EPC Check")]
        public ETestResult IntegraMarginReadEPCCheck { get; set; } = ETestResult.None;

        [Name("Integra: Memory Self-Check")]
        public ETestResult IntegraMemorySelfCheck { get; set; } = ETestResult.None;
    }
}
