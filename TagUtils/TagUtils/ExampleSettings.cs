using Newtonsoft.Json;

namespace Impinj.TagUtils
{
    public abstract class ExampleSettings
    {
        [JsonIgnore]
        public bool ChangesWereMade { get; set; } = false;

        [JsonIgnore]
        public bool OverrideChanges { get; set; } = false;
    }
}
