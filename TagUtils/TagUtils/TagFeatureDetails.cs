using Newtonsoft.Json;
using System;

namespace Impinj.TagUtils
{
    public class TagFeatureDetails : ICloneable
    {
        [JsonProperty("Is Supported")]
        public bool? IsSupported { get; set; } = new bool?();

        public string Description { get; set; } = null;

        public object Clone() => MemberwiseClone();
    }
}
