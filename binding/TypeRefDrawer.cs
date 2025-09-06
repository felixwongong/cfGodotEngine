using System;
using cfGodotEngine.UI;
using Godot;

namespace cfGodotEngine.Binding;

[CustomPropertyDrawer]
public partial class TypeRefDrawer : CustomPropertyDrawer
{
    private SearchableOptionDropdown _searchableOptionButton;

    protected override void _BuildNode(PropertyHint hintType, string hintString)
    {
        _searchableOptionButton = new SearchableOptionDropdown()
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
                    _searchableOptionButton.AddItem(type.AssemblyQualifiedName, type.Name);
                }
            }
        }
        
        AddChild(_searchableOptionButton);
    }

    protected override void OnPropertyUpdated(Variant propertyValue)
    {
    }

    protected override void _ClearNode()
    {
    }
}