using System;
using cfGodotEngine.UI;
using Godot;

namespace cfGodotEngine.Binding;

[CustomPropertyDrawer]
public partial class TypeRefDrawer : CustomPropertyDrawer
{
    private OptionDropdown optionButton;

    protected override void _BuildNode(PropertyHint hintType, string hintString)
    {
        optionButton = new OptionDropdown()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };

        if (string.IsNullOrEmpty(hintString))
        {
            GD.PrintErr("TypeRefDrawer: Missing base type hint string");
            return;
        }

        var baseType = Type.GetType(hintString);
        if (baseType == null)
        {
            GD.PrintErr($"TypeRefDrawer: Invalid base type {hintString}");
            return;
        }
        
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (baseType.IsAssignableFrom(type))
                {
                    optionButton.AddItem(type.AssemblyQualifiedName, type.Name);
                }
            }
        }
        
        AddChild(optionButton);
    }

    protected override void OnPropertyUpdated(Variant propertyValue)
    {
    }

    protected override void _ClearNode()
    {
    }
}