using System;
using cfGodotEngine.UI;
using Godot;

namespace cfGodotEngine.binding;

[CustomPropertyDrawer]
public partial class TypeRefDrawer(PropertyHint hintType, string hintString) : CustomPropertyDrawer(hintType, hintString)
{
    private SearchableDropdown optionButton;

    protected override void _BuildNode()
    {
        optionButton = new SearchableDropdown()
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
        
        int id = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (baseType.IsAssignableFrom(type))
                {
                    optionButton.AddItem(type.Name, id++);
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