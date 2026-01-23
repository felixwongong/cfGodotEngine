using System;

namespace cfGodotEngine.Binding;

public enum BindingAccessibility
{
    Public,
    Protected,
    Private,
    Internal
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public class PropertyBindingAttribute: Attribute
{
    public string name;
    public BindingAccessibility accessibility;

    public PropertyBindingAttribute(string name = "", BindingAccessibility accessibility = BindingAccessibility.Public)
    {
        this.name = name;
        this.accessibility = accessibility;
    } 
}