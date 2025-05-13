using Impinj.TagUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.IO;

namespace Impinj.Utils
{
    public static class JsonConvertExtensions
    {
        public static T ToEnum<T>(this string value) => JsonConvert.DeserializeObject<T>("\"" + value + "\"");

        public static void SerializeToFile<T>(this T target, string fileName)
        {
            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                NullValueHandling = NullValueHandling.Ignore,
                TypeNameHandling = TypeNameHandling.Auto
            };
            settings.Converters.Add(new StringEnumConverter());
            settings.Converters.Add((JsonConverter)new NumberRangesJsonConverter());
            try
            {
                string contents = JsonConvert.SerializeObject(target, Formatting.Indented, settings);
                File.WriteAllText(fileName, contents);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error! Something went wrong saving " + fileName + ": " + ex.Message);
                throw;
            }
        }

        public static T LoadFromFile<T>(string fileName)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(fileName), new JsonSerializerSettings()
                {
                    TypeNameHandling = TypeNameHandling.Auto
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error! Something went wrong loading " + fileName + ": " + ex.Message);
                throw;
            }
        }
    }
}
