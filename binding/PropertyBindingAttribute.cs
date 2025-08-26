using System;

namespace cfGodotEngine.Binding;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public class PropertyBindingAttribute: Attribute
{
    public string name;

    public PropertyBindingAttribute(string name = "")
    {
        this.name = name;
    } 
}