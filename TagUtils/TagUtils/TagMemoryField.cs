using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Impinj.TagUtils
{
    public class TagMemoryField : ICloneable
    {
        [JsonProperty("Is Lockable")]
        public bool? IsLockable { get; set; } = new bool?();

        [JsonConverter(typeof(NumberRangesJsonConverter))]
        public List<NumberRange> Bits { get; set; } = null;

        [JsonConverter(typeof(NumberRangesJsonConverter))]
        public List<NumberRange> Blocks { get; set; } = null;

        [JsonConverter(typeof(NumberRangesJsonConverter))]
        [JsonProperty("Lockable Bits")]
        public List<NumberRange> LockableBits { get; set; } = null;

        public string Value { get; set; } = null;

        public string Description { get; set; } = null;

        public List<TagMemoryField> Dynamic { get; set; } = null;

        [JsonProperty("Depends On")]
        public Dictionary<TagAccessLocation, TagMemoryField> DependsOn { get; set; } = null;

        [JsonProperty("Applies To")]
        public Dictionary<TagAccessLocation, TagMemoryField> AppliesTo { get; set; } = null;

        public string Field { get; set; } = null;

        [JsonConstructor]
        public TagMemoryField(string bitsString = null, string value = null)
        {
            Bits = new List<NumberRange>().AddFrom(bitsString);
            Value = value;
        }

        public TagMemoryField(long bit, string value = null)
          : this(bit.ToString(), value)
        {
        }

        public object Clone()
        {
            TagMemoryField tagMemoryField1 = (TagMemoryField)MemberwiseClone();
            if (Dynamic != null)
            {
                tagMemoryField1.Dynamic = new List<TagMemoryField>();
                tagMemoryField1.Dynamic.AddRange(Dynamic.Select(dynamo => (TagMemoryField)dynamo.Clone()));
            }
            if (Bits != null)
            {
                tagMemoryField1.Bits = new List<NumberRange>();
                tagMemoryField1.Bits.AddRange(Bits.Select(numberRange =>
                {
                    NumberRange numberRange1 = numberRange;
                    long min = numberRange1.Min;
                    numberRange1 = numberRange;
                    long max = numberRange1.Max;
                    return new NumberRange(min, max);
                }));
            }
            if (DependsOn != null)
                tagMemoryField1.DependsOn = DependsOn.Select(dependsOn =>
                {
                    KeyValuePair<TagAccessLocation, TagMemoryField> keyValuePair = dependsOn;
                    int key = (int)keyValuePair.Key;
                    keyValuePair = dependsOn;
                    TagMemoryField tagMemoryField2 = (TagMemoryField)keyValuePair.Value.Clone();
                    return new
                    {
                        memory = (TagAccessLocation)key,
                        field = tagMemoryField2
                    };
                }).ToDictionary(d => d.memory, d => d.field);
            return tagMemoryField1;
        }
    }
}
