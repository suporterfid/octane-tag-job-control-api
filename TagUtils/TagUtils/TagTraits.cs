using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;


#nullable enable
namespace Impinj.TagUtils
{
    public class TagTraits : ICloneable
    {
        public
#nullable disable
        string Description
        { get; set; } = null;

        public Dictionary<string, TagFeatureDetails> Features { get; set; } = new Dictionary<string, TagFeatureDetails>();

        [JsonProperty("Memory Bank")]
        public Dictionary<TagAccessLocation, TagMemoryBank> Memory { get; set; } = new Dictionary<TagAccessLocation, TagMemoryBank>();

        public object Clone()
        {
            TagTraits tagTraits = (TagTraits)MemberwiseClone();
            if (Features != null)
                tagTraits.Features = Features.Select(feature =>
                {
                    KeyValuePair<string, TagFeatureDetails> keyValuePair = feature;
                    string key = keyValuePair.Key;
                    keyValuePair = feature;
                    TagFeatureDetails tagFeatureDetails = (TagFeatureDetails)keyValuePair.Value.Clone();
                    return new
                    {
                        feature = key,
                        details = tagFeatureDetails
                    };
                }).ToDictionary(f => f.feature, f => f.details);
            if (Memory != null)
                tagTraits.Memory = Memory.Select(memory =>
                {
                    KeyValuePair<TagAccessLocation, TagMemoryBank> keyValuePair = memory;
                    int key = (int)keyValuePair.Key;
                    keyValuePair = memory;
                    TagMemoryBank tagMemoryBank = (TagMemoryBank)keyValuePair.Value.Clone();
                    return new
                    {
                        location = (TagAccessLocation)key,
                        details = tagMemoryBank
                    };
                }).ToDictionary(m => m.location, m => m.details);
            return tagTraits;
        }

        internal void Merge(TagTraits entry)
        {
            Description = entry.Description ?? Description;
            foreach (KeyValuePair<string, TagFeatureDetails> feature in entry.Features)
            {
                TagFeatureDetails tagFeatureDetails;
                if (Features.TryGetValue(feature.Key, out tagFeatureDetails))
                {
                    tagFeatureDetails.Description = feature.Value.Description ?? tagFeatureDetails.Description;
                    tagFeatureDetails.IsSupported = feature.Value.IsSupported ?? tagFeatureDetails.IsSupported;
                }
                else
                    Features[feature.Key] = feature.Value;
            }
            foreach (KeyValuePair<TagAccessLocation, TagMemoryBank> keyValuePair in entry.Memory)
            {
                if (!Memory.ContainsKey(keyValuePair.Key))
                    Memory.Add(keyValuePair.Key, keyValuePair.Value);
            }
            foreach (KeyValuePair<TagAccessLocation, TagMemoryBank> keyValuePair in Memory)
            {
                if (entry.Memory.ContainsKey(keyValuePair.Key))
                {
                    foreach (KeyValuePair<string, TagMemoryField> field in entry.Memory[keyValuePair.Key].Fields)
                    {
                        TagMemoryField tagMemoryField;
                        if (keyValuePair.Value.Fields.TryGetValue(field.Key, out tagMemoryField))
                        {
                            tagMemoryField.Bits = field.Value.Bits ?? tagMemoryField.Bits;
                            tagMemoryField.DependsOn = field.Value.DependsOn ?? tagMemoryField.DependsOn;
                            tagMemoryField.Description = field.Value.Description ?? tagMemoryField.Description;
                            tagMemoryField.Dynamic = field.Value.Dynamic ?? tagMemoryField.Dynamic;
                            tagMemoryField.Value = field.Value.Value ?? tagMemoryField.Value;
                            tagMemoryField.AppliesTo = field.Value.AppliesTo ?? tagMemoryField.AppliesTo;
                        }
                        else
                            keyValuePair.Value.Fields[field.Key] = field.Value;
                    }
                }
            }
        }
    }
}
