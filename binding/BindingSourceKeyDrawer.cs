using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cfGodotEngine.UI;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

[CustomPropertyDrawer]
public partial class BindingSourceKeyDrawer : CustomPropertyDrawer
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
            placeHolderText = "Select source key...",
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

        RefreshKeys();
    }

    public override void _Ready()
    {
        base._Ready();
        RefreshKeys();
        RefreshSelection();
        RefreshWarning();
    }

    protected override void OnPropertyUpdated(Variant propertyValue)
    {
        if (!this.IsAlive())
            return;

        RefreshKeys();
        RefreshSelection();
        RefreshWarning();
    }

    protected override void _ClearNode()
    {
        if (_dropdown.IsAlive()) _dropdown.Clear();
        if (_warningLabel.IsAlive()) _warningLabel.Visible = false;
    }

    private void RefreshKeys()
    {
        if (_dropdown == null || !_dropdown.IsAlive())
            return;

        _dropdown.Clear();
        var keys = GetSourceKeys();
        foreach (var key in keys)
            _dropdown.AddItem(key);
    }

    private void RefreshSelection()
    {
        if (_dropdown == null || !_dropdown.IsAlive())
            return;

        var value = GetEditedPropertyValue();
        var id = value.VariantType == Variant.Type.Nil ? string.Empty : (string)value;
        _dropdown.Select(id);
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
    }

    private IEnumerable<string> GetSourceKeys()
    {
        var sourceType = GetEditedObjectField<StringName>("_sourceType");
        if (sourceType == null || sourceType.IsEmpty)
            return Array.Empty<string>();

        var type = ResolveType(sourceType);
        if (type == null)
            return Array.Empty<string>();

        var method = type.GetMethod("GetBindingKeys",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (method == null)
            return Array.Empty<string>();

        return method.Invoke(null, null) as IEnumerable<string> ?? Array.Empty<string>();
    }

    private static Type ResolveType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try
            {
                type = assembly.GetType(typeName);
            }
            catch
            {
                continue;
            }

            if (type != null)
                return type;
        }
        return null;
    }
}
