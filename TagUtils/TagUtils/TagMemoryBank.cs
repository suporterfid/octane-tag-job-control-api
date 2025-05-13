using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;


#nullable enable
namespace Impinj.TagUtils
{
    public class TagMemoryBank : ICloneable
    {
        public const int HEX_CHARS_PER_WORD = 4;
        public const int BITS_PER_NIBBLE = 4;
        public const int BITS_PER_BYTE = 8;
        public const int BITS_PER_WORD = 16;
        public const int BITS_PER_32_WORDS = 512;

        public
#nullable disable
        Dictionary<string, TagMemoryField> Fields
        { get; set; } = new Dictionary<string, TagMemoryField>();

        [JsonProperty("Is Lockable")]
        public bool? IsLockable { get; set; } = new bool?();

        public object Clone()
        {
            TagMemoryBank tagMemoryBank = (TagMemoryBank)MemberwiseClone();
            if (Fields != null)
                tagMemoryBank.Fields = Fields.Select(field =>
                {
                    KeyValuePair<string, TagMemoryField> keyValuePair = field;
                    string key = keyValuePair.Key;
                    keyValuePair = field;
                    TagMemoryField tagMemoryField = (TagMemoryField)keyValuePair.Value.Clone();
                    return new { name = key, details = tagMemoryField };
                }).ToDictionary(f => f.name, f => f.details);
            return tagMemoryBank;
        }
    }
}
