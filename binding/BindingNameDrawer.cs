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
    private OptionDropdown sourceTypeSelector;
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
            sourceTypeSelector = new OptionDropdown()
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 1,
            };
            
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (typeof(IBindingSource).IsAssignableFrom(type) && type != typeof(IBindingSource))
                    {
                        sourceTypeSelector.AddItem(type.AssemblyQualifiedName, type.Name);
                    }
                }
            }
            
            if(bindingName != null && !string.IsNullOrWhiteSpace(bindingName.typeName)) {
                sourceTypeSelector.Select(bindingName.typeName);
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
            bindingNameField.TextChanged += OnBindingNameUpdated;
        }
        
        container.AddChild(sourceTypeSelector);
        container.AddChild(bindingNameOption);
        container.AddChild(bindingNameField);
        
        AddChild(container);

        void OnBindingNameUpdated(string input)
        {
            var bindingName = GetPropertyValue().As<BindingName>() ?? new BindingName();
            bindingName.propertyName = input;
            SetPropertyValue(bindingName);
        }

        void OnBindingNameSelected(long index)
        {
            bindingNameField.Text = bindingNameOption.GetItemText((int)index);
            OnBindingNameUpdated(bindingNameField.Text);
        }

        void OnSourceTypeSelected(string qualifiedName, string displayName)
        {
            var bindingName = GetPropertyValue().As<BindingName>() ?? new BindingName();
            bindingName.typeName = qualifiedName;
            SetPropertyValue(bindingName);
        }
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
            GD.Print("Updated BindingNameDrawer with type:", bindingName.typeName);
            sourceTypeSelector.Select(bindingName.typeName);
            bindingNameOption.Clear();
            var type = Type.GetType($"{bindingName.typeName}");
            if (type == null)
            {
                GD.PrintErr($"BindingNameDrawer: Cannot find type {bindingName.typeName}");
                return;
            }

            var method = type.GetMethod("GetBindingKeys", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                GD.PrintErr($"BindingNameDrawer: Cannot find static method GetBindingKeys in type {type.FullName}");
                return;
            }

            var keys = method.Invoke(null, null) as IEnumerable<string>;
            if (keys == null)
            {
                GD.PrintErr($"BindingNameDrawer: GetBindingKeys did not return IEnumerable<string> in type {type.FullName}");
                return;
            }
            
            foreach (var key in keys)
            {
                bindingNameOption.AddItem(key);
            }
        }
        
        bindingNameField.Text = bindingName.propertyName ?? "";
    }

    protected override void _ClearNode()
    {
    }
}