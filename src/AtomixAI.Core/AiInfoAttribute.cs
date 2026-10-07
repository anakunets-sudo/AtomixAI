using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;

namespace AtomixAI.Core
{
    [AttributeUsage(AttributeTargets.Class)]
    public class AiInfoAttribute : Attribute
    {
        public string Name { get; }
        public string Group { get; }
        public string Description { get; }
        public string[] Keywords { get; }

        public AiInfoAttribute(string name, AtomicGroupType group, string description, params string[] keywords)
        {
            Name = name;
            Group = Enum.GetName(typeof(AtomicGroupType), group);
            Description = description;
            Keywords = keywords;
        }
    }
}