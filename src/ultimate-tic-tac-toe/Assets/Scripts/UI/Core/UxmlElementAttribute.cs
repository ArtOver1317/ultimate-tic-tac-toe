using System;

namespace UI.Core
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class UxmlElementAttribute : Attribute
    {
        public string Name { get; }
        public bool IsOptional { get; }

        public UxmlElementAttribute(string name = null, bool isOptional = false)
        {
            Name = name;
            IsOptional = isOptional;
        }
    }
}

