using Impinj.OctaneSdk;
using Impinj.TagUtils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Impinj.Utils
{
    public static class OctaneSDKExtensions
    {
        public static MemoryBank ToOctaneSDKMemoryBank(this TagAccessLocation tagAccessLocation)
        {
            switch (tagAccessLocation)
            {
                case TagAccessLocation.Reserved:
                case TagAccessLocation.KillPassword:
                case TagAccessLocation.AccessPassword:
                    return MemoryBank.Reserved;
                case TagAccessLocation.Epc:
                case TagAccessLocation.Tid:
                    return (MemoryBank)tagAccessLocation;
                case TagAccessLocation.User:
                    return MemoryBank.User;
                default:
                    throw new Exception("Unsupported TagReadOp! '" + tagAccessLocation.ToString() + "'");
            }
        }

        public static string ToEnumMemberAttrValue(this Enum @enum)
        {
            MemberInfo memberInfo = @enum.GetType().GetMember(@enum.ToString()).FirstOrDefault();
            EnumMemberAttribute enumMemberAttribute = (object)memberInfo != null ? memberInfo.GetCustomAttributes(false).OfType<EnumMemberAttribute>().FirstOrDefault() : null;
            return enumMemberAttribute == null ? @enum.ToString() : enumMemberAttribute.Value;
        }

        public static T FromEnumMemberAttrValue<T>(this string str)
        {
            Type enumType = typeof(T);
            IEnumerable<string> source = Enum.GetNames(enumType).Where(name => ((IEnumerable<EnumMemberAttribute>)enumType.GetField(name).GetCustomAttributes(typeof(EnumMemberAttribute), true)).Single().Value == str);
            return source.Any() ? (T)Enum.Parse(enumType, source.ElementAt(0)) : default;
        }
    }
}
