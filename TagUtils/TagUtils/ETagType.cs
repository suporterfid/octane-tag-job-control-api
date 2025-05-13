using System.Runtime.Serialization;

namespace Impinj.TagUtils
{
    [DataContract]
    public enum ETagType
    {
        [EnumMember(Value = "All Impinj Tags")] Unknown,
        [EnumMember(Value = "Impinj Monza X-2K")] ImpinjMonzaX2K,
        [EnumMember(Value = "Impinj Monza X-8K")] ImpinjMonzaX8K,
        [EnumMember(Value = "Impinj Monza 4D")] ImpinjMonza4D,
        [EnumMember(Value = "Impinj Monza 4E")] ImpinjMonza4E,
        [EnumMember(Value = "Impinj Monza 4QT")] ImpinjMonza4QT,
        [EnumMember(Value = "Impinj Monza 4i")] ImpinjMonza4i,
        [EnumMember(Value = "Impinj Monza 5")] ImpinjMonza5,
        [EnumMember(Value = "Impinj Monza R6")] ImpinjMonzaR6,
        [EnumMember(Value = "Impinj Monza R6-P")] ImpinjMonzaR6P,
        [EnumMember(Value = "Impinj Monza R6-A")] ImpinjMonzaR6A,
        [EnumMember(Value = "Impinj Monza R6-B")] ImpinjMonzaR6B,
        [EnumMember(Value = "Impinj M750")] ImpinjM750,
        [EnumMember(Value = "Impinj M730")] ImpinjM730,
    }
}
