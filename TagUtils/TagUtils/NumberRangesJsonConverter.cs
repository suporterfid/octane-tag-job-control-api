using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Impinj.TagUtils
{
    public sealed class NumberRangesJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => typeof(long).Equals(objectType);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) => writer.WriteValue((value as List<NumberRange>).ToHexString());

        public override object ReadJson(
          JsonReader reader,
          Type objectType,
          object existingValue,
          JsonSerializer serializer)
        {
            return new List<NumberRange>().AddFrom(reader.Value as string);
        }
    }
}
