using System;
using System.Collections.Generic;
using System.Reflection;
using cfGodotEngine.UI;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

[CustomPropertyDrawer]
public partial class BindingNameDrawer : CustomPropertyDrawer
{
    private SearchableOptionDropdown sourceTypeOption;
    private OptionButton nameOption;
    
    protected override void _BuildNode(PropertyHint hintType, string hintString)
    {
        var container = new HBoxContainer()
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
        };

        {
            sourceTypeOption = new SearchableOptionDropdown()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 1,
            };
            
            sourceTypeOption.OnSelected += OnSourceTypeSelected;
        }

        {
            nameOption = new OptionButton()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 1,
            };
            nameOption.ItemSelected += i => OnNameUpdated(nameOption.GetItemText((int)i));
        }
        
        container.AddChild(sourceTypeOption);
        container.AddChild(nameOption);
        
        AddChild(container);

        void OnNameUpdated(string name)
        {
            if (nameOption.ItemCount == 0)
                nameOption.AddItem(string.Empty);
            nameOption.Select(0);
            
            for (int i = 0; i < nameOption.ItemCount; i++)
            {
                var option = nameOption.GetItemText(i);
                if (option.Equals(name))
                {
                    nameOption.Select(i);
                    break;
                }
            }
            
            var bindingName = GetPropertyValue().As<BindingName>() ?? new BindingName();
            bindingName.propertyName = name;
            SetPropertyValue(bindingName);
        }

        void OnSourceTypeSelected(string qualifiedName, string displayName)
        {
            var bindingName = GetPropertyValue().As<BindingName>() ?? new BindingName();
            bindingName.typeName = qualifiedName;
            SetPropertyValue(bindingName);
        }
    }

    public override void _Ready()
    {
        base._Ready();
        
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes())
            {
                if (typeof(IBindingSource).IsAssignableFrom(type) && type != typeof(IBindingSource))
                {
                    sourceTypeOption.AddItem(type.AssemblyQualifiedName, type.Name);
                }
            }
        }

        var bindingName = GetPropertyValue().As<BindingName>();
        if(bindingName != null && !string.IsNullOrWhiteSpace(bindingName.typeName)) {
            sourceTypeOption.Select(bindingName.typeName);
        } 
    }

    protected override void OnPropertyUpdated(Variant propertyValue)
    {
        if(!this.IsAlive())
            return;
            
        var bindingName = propertyValue.As<BindingName>();
        if (bindingName == null)
        {
            _ClearNode();
            return;
        }

        if (!string.IsNullOrWhiteSpace(bindingName.typeName))
        {
            sourceTypeOption.Select(bindingName.typeName);
            nameOption.Clear();
            var type = Type.GetType($"{bindingName.typeName}");
            if (type == null)
            {
                GD.PrintErr($"BindingNameDrawer: Cannot find type {bindingName.typeName}");
                return;
            }

            var method = type.GetMethod("GetBindingKeys", BindingFlags.Public | BindingFlags.Static);
            if (method?.Invoke(null, null) is not IEnumerable<string> keys)
            {
                GD.PrintErr($"BindingNameDrawer: Invalid or method GetBindingKeys not found in type {bindingName.typeName}");
                return;
            }
            
            int index = 0;
            nameOption.AddItem(string.Empty);
            foreach (var key in keys)
            {
                index++;
                nameOption.AddItem(key);

                if (key.Equals(bindingName.propertyName))
                    nameOption.Select(index);
            }
        }
    }

    protected override void _ClearNode()
    {
        if (sourceTypeOption.IsAlive()) sourceTypeOption.Deselect();
        if (nameOption.IsAlive()) nameOption.Clear();
    }
}