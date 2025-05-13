using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Impinj.TagUtils
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum TagAccessOperation
    {
        None,
        Read,
        Write,
        BlockWrite,
        MarginRead,
        GetQt,
        SetQt,
        Lock,
        Permalock,
        BlockPermalock,
        Permaunlock,
        Unlock,
        Kill,
        Test,
    }
}
