using System;
using System.Reflection;
using cfGodotEngine.UI;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

[CustomPropertyDrawer]
public partial class BindingNameDrawer : CustomPropertyDrawer
{
    private SearchableDropdown sourceTypeSelector;
    private OptionButton bindingNameOption;
    private LineEdit bindingNameField;
    
    protected override void _BuildNode(PropertyHint hintType, string hintString)
    {
        var bindingName = GetPropertyValue().As<BindingName>();
        
        var container = new HBoxContainer()
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
        };

        {
            sourceTypeSelector = new SearchableDropdown()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 1,
            };
            
            int id = 0;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (typeof(IBindingSource).IsAssignableFrom(type))
                    {
                        sourceTypeSelector.AddItem(type.Name, id++);
                    }
                }
            }
            
            if(bindingName != null && !string.IsNullOrWhiteSpace(bindingName.typeName)) {
                sourceTypeSelector.Accept(bindingName.typeName);
            } 
            
            sourceTypeSelector.OnSelected += OnSourceTypeSelected;
        }

        {
            bindingNameOption = new OptionButton()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 1,
            };
            bindingNameOption.ItemSelected += OnBindingNameSelected;
            
            bindingNameField = new LineEdit()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 2,
            };
        }
        
        container.AddChild(sourceTypeSelector);
        container.AddChild(bindingNameOption);
        container.AddChild(bindingNameField);
        AddChild(container);
    }

    private void OnBindingNameSelected(long index)
    {
    }

    private void OnSourceTypeSelected(int id, string type)
    {
        var bindingName = GetPropertyValue().As<BindingName>() ?? new BindingName();
        bindingName.typeName = type;
        SetPropertyValue(bindingName);
    }

    protected override void OnPropertyUpdated(Variant propertyValue)
    {
        var bindingName = propertyValue.As<BindingName>();
        if (bindingName == null)
        {
            _ClearNode();
            return;
        }

        if (!string.IsNullOrWhiteSpace(bindingName.typeName))
        {
            sourceTypeSelector.Accept(bindingName.typeName);
            bindingNameOption.Clear();
            var type = Type.GetType($"{bindingName.typeName}.BindingKey");
            if (type == null)
            {
                GD.PrintErr($"BindingNameDrawer: Cannot find type {bindingName.typeName}.BindingKey");
                return;
            }

            var method = type.GetMethod("GetBindingKeys", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                GD.PrintErr($"BindingNameDrawer: Cannot find static method GetBindingKeys in type {type.FullName}");
                return;
            }
            
            var keys = method.Invoke(null, null) as string[];
            foreach (var key in keys)
            {
                bindingNameOption.AddItem(key);
            }
        }
    }

    protected override void _ClearNode()
    {
        sourceTypeSelector?.Clear();
        bindingNameOption?.Clear();
        bindingNameField?.Clear();
    }
}