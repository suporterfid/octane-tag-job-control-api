using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace Impinj.TagUtils
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TagAccessLocation
    {
        Reserved,
        Epc,
        Tid,
        User,
        [EnumMember(Value = "Kill Password")] KillPassword,
        [EnumMember(Value = "Access Password")] AccessPassword,
        EntireTag,
        None,
    }
}
