using Newtonsoft.Json;
using System;

namespace Impinj.TagUtils
{
    public class TagOperation
    {
        [JsonProperty(Order = 1)]
        public TagAccessOperation Operation { get; protected set; } = TagAccessOperation.None;

        public virtual IExecutedTagOperation ToExecutedTagOperation() => throw new InvalidOperationException("Not allowed!");
    }
}
