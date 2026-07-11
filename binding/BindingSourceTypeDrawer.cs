using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cfGodotEngine.Binding;
using cfGodotEngine.UI;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

[CustomPropertyDrawer]
public partial class BindingSourceTypeDrawer : CustomPropertyDrawer
{
    private SearchableOptionDropdown _dropdown;
    private Label _warningLabel;

    protected override void _BuildNode(PropertyHint hintType, string hintString)
    {
        var vb = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        _dropdown = new SearchableOptionDropdown
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            placeHolderText = "Select source type...",
        };
        _dropdown.OnSelected += OnSelected;

        _warningLabel = new Label
        {
            Visible = false,
            Modulate = new Color(1f, 0.4f, 0.4f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };

        vb.AddChild(_dropdown);
        vb.AddChild(_warningLabel);
        AddChild(vb);

        PopulateTypes();
    }

    public override void _Ready()
    {
        base._Ready();
        PopulateTypes();
        RefreshSelection();
        RefreshWarning();
    }

    protected override void OnPropertyUpdated(Variant propertyValue)
    {
        if (!this.IsAlive())
            return;

        RefreshSelection();
        RefreshWarning();
    }

    protected override void _ClearNode()
    {
        if (_dropdown.IsAlive()) _dropdown.Clear();
        if (_warningLabel.IsAlive()) _warningLabel.Visible = false;
    }

    private void PopulateTypes()
    {
        if (_dropdown == null || !_dropdown.IsAlive())
            return;

        _dropdown.Clear();
        var types = GetBindingSourceTypes();
        foreach (var type in types)
            _dropdown.AddItem(type.FullName, type.Name);
    }

    private void RefreshSelection()
    {
        if (_dropdown == null || !_dropdown.IsAlive())
            return;

        var value = GetEditedPropertyValue();
        var id = value.VariantType == Variant.Type.Nil ? string.Empty : (string)value;
        _dropdown.SelectOrAdd(id, id);
    }

    private void RefreshWarning()
    {
        if (_warningLabel == null || !_warningLabel.IsAlive())
            return;

        var editedObject = GetEditedObject();
        if (editedObject is Binder binder)
        {
            var error = binder.ValidateBinding();
            _warningLabel.Visible = !string.IsNullOrEmpty(error);
            _warningLabel.Text = error;
        }
    }

    private void OnSelected(string id, string displayName)
    {
        SetPropertyValue(new StringName(id));
        if (GetEditedObject() is GodotObject obj)
            obj.NotifyPropertyListChanged();
    }

    private static IEnumerable<Type> GetBindingSourceTypes()
    {
        var result = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types)
            {
                if (typeof(IBindingSource).IsAssignableFrom(type) && type != typeof(IBindingSource) && !type.IsInterface)
                    result.Add(type);
            }
        }
        return result.OrderBy(t => t.Name);
    }
}
