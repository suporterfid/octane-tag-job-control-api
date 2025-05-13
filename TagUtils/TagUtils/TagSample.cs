using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Impinj.TagUtils
{
    public class TagSample
    {
        [JsonIgnore]
        public Dictionary<TagAccessLocation, string> BankData = new Dictionary<TagAccessLocation, string>();

        public string Type { get; private set; } = "Unknown Tag Type";

        [JsonIgnore]
        public TagLibrary Library { get; private set; } = null;

        [JsonIgnore]
        public TagTraits Traits { get; private set; } = null;

        public string BackscatteredEPC { get; private set; } = string.Empty;

        [JsonIgnore]
        public HashSet<string> OperationsScheduled { get; } = new HashSet<string>();

        public string EPC => BackscatteredEPC;

        public string TID => BankData[TagAccessLocation.Tid];

        [JsonIgnore]
        public ETestResult MarginReadCheck { get; set; } = ETestResult.None;

        [JsonIgnore]
        public string MarginReadErrorMessage { get; set; } = string.Empty;

        [JsonIgnore]
        public bool LikelyInPublicMode { get; internal set; } = false;

        public TagSample(TagLibrary library, string backscatteredEpc = "", string tid = "")
        {
            Library = library;
            BackscatteredEPC = backscatteredEpc;
            BankData[TagAccessLocation.Tid] = tid;
            RefreshFields();
        }

        public bool RefreshFields()
        {
            if (Library != null)
                (Type, Traits) = Library.GetTagDetails(BankData[TagAccessLocation.Tid]);
            return ApplyDynamicFields();
        }

        private bool ApplyDynamicFields()
        {
            if (Traits == null)
                return false;
            bool flag1 = true;
            foreach (KeyValuePair<TagAccessLocation, TagMemoryBank> keyValuePair in Traits.Memory)
            {
                foreach (TagMemoryField tagMemoryField1 in keyValuePair.Value.Fields.Values.Where(f => f.Dynamic != null))
                {
                    bool flag2 = false;
                    foreach (TagMemoryField tagMemoryField2 in tagMemoryField1.Dynamic)
                    {
                        TagAccessLocation key = tagMemoryField2.DependsOn.Keys.First();
                        string hex;
                        if (BankData.TryGetValue(key, out hex) && tagMemoryField2.DependsOn[key].Bits.ApplyToHexValue(hex).AddPrefix(16) == tagMemoryField2.DependsOn[key].Value.AddPrefix(16))
                        {
                            tagMemoryField1.AppliesTo = tagMemoryField2.AppliesTo ?? tagMemoryField1.AppliesTo;
                            tagMemoryField1.Bits = tagMemoryField2.Bits ?? tagMemoryField1.Bits;
                            tagMemoryField1.Description = tagMemoryField2.Description ?? tagMemoryField1.Description;
                            tagMemoryField1.Value = tagMemoryField2.Value ?? tagMemoryField1.Value;
                            flag2 = true;
                            break;
                        }
                    }
                    flag1 &= flag2;
                }
            }
            return flag1;
        }
    }
}

