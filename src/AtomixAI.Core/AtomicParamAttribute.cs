using System;
using System.Collections.Generic;
using System.Text;

namespace AtomixAI.Core
{
    [AttributeUsage(AttributeTargets.Property)]
    public class AtomicParamAttribute : Attribute
    {
        public string Description { get; }
        public bool IsRequired { get; }

        public AtomicParamAttribute(string description, bool isRequired = false)
        {
            Description = description;
            IsRequired = isRequired;
        }
    }
}