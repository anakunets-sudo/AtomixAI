using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace AtomixAI.Core
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class AiParamAttribute : Attribute
    { 
        public string Description { get; }
        public string Type { get; }
        public bool IsRequired { get; }

        public Type SchemaType { get; }

        public AiParamAttribute(string description, bool isRequired = false)
        {
            Description = description;
            IsRequired = isRequired;
        }

        public AiParamAttribute(string description, string type, bool isRequired = false)
        {
            Description = description;
            Type = type;
            IsRequired = isRequired;
        }

        public AiParamAttribute(Type schema, string type, bool isRequired = true)
        {
            SchemaType = schema;
            Type = type;
            IsRequired = isRequired;
        }
    }
}