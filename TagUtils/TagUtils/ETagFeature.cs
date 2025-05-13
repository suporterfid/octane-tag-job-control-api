using System.Runtime.Serialization;

namespace Impinj.TagUtils
{
    public enum ETagFeature
    {
        Unknown,
        [EnumMember(Value = "Password Killable")] Kill,
        [EnumMember(Value = "Password Lockable")] Lock,
        [EnumMember(Value = "EPC Permalockable")] EPCPermalock,
        [EnumMember(Value = "EPC AFI Bits Writable")] EPCAFIBitsWrite,
        [EnumMember(Value = "BlockWrite")] BlockWrite,
        [EnumMember(Value = "BlockPermalock")] BlockPermalock,
        [EnumMember(Value = "Untraceable Short Range")] UntraceableShortRange,
        [EnumMember(Value = "FastID")] FastID,
        [EnumMember(Value = "TagFocus")] TagFocus,
        [EnumMember(Value = "Integra TID Even Parity Check")] IntegraTIDEvenParityCheck,
        [EnumMember(Value = "Integra Memory Self Check")] IntegraMemorySelfCheck,
        [EnumMember(Value = "Integra MarginRead")] IntegraMarginRead,
        QT,
        [EnumMember(Value = "Protected Mode")] ProtectedMode,
        [EnumMember(Value = "Enhanced Integra Automatic Memory Parity Check")] EnhancedIntegraAutomaticMemoryParityCheck,
        [EnumMember(Value = "Short Range Capability")] ShortRangeCapability,
        AutoTune,
        [EnumMember(Value = "I2C Interface")] I2CInterface,
    }
}
