using Impinj.TagUtils;
using Impinj.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Impinj.TagUtils
{
    public class TagLibrary
    {
        private const string IMPINJ_TAG_MEMORY_LAYOUTS_JSON_FILE = "ImpinjTagMemoryLayouts.json";

        [JsonProperty("Schema Version")]
        public string SchemaVersion { get; set; } = "1.0.0.17";

        [JsonProperty("Tag Design")]
        private Dictionary<string, TagTraits> TagDesign { get; set; } = new Dictionary<string, TagTraits>();

        public (string, TagTraits) GetTagDetails(string tid)
        {
            string str1 = ETagType.Unknown.ToEnumMemberAttrValue();
            TagTraits tagTraits = (TagTraits)TagDesign[ETagType.Unknown.ToEnumMemberAttrValue()].Clone();
            if (string.IsNullOrWhiteSpace(tid))
                return (str1, tagTraits);
            foreach (string key in TagDesign.Keys)
            {
                if (!(key == ETagType.Unknown.ToEnumMemberAttrValue()))
                {
                    TagMemoryField field = TagDesign[key].Memory[TagAccessLocation.Tid].Fields[ETagBitField.Id.ToEnumMemberAttrValue()];
                    string str2 = field.Value.Substring(2);
                    if (field.Bits.ApplyToHexValue(tid).TrimStart('0') == str2)
                    {
                        str1 = key;
                        tagTraits.Merge((TagTraits)TagDesign[key].Clone());
                        break;
                    }
                }
            }
            return (str1, tagTraits);
        }

        public IEnumerable<KeyValuePair<string, TagTraits>> GetTagTraits() => TagDesign.Where(k => k.Key != ETagType.Unknown.ToEnumMemberAttrValue());

        public void SerializeToFile(string fileName) => this.SerializeToFile<TagLibrary>(fileName);

        public static TagLibrary LoadFromFile(string fileName) => JsonConvertExtensions.LoadFromFile<TagLibrary>(fileName);
    }
}
