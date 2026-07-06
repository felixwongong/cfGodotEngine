using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using cfGodotEngine.UI;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding;

[CustomPropertyDrawer]
public partial class BindingTargetPropertyDrawer : CustomPropertyDrawer
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
            placeHolderText = "Select target property...",
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

        RefreshProperties();
    }

    public override void _Ready()
    {
        base._Ready();
        RefreshProperties();
        RefreshSelection();
        RefreshWarning();
    }

    protected override void OnPropertyUpdated(Variant propertyValue)
    {
        if (!this.IsAlive())
            return;

        RefreshProperties();
        RefreshSelection();
        RefreshWarning();
    }

    protected override void _ClearNode()
    {
        if (_dropdown.IsAlive()) _dropdown.Clear();
        if (_warningLabel.IsAlive()) _warningLabel.Visible = false;
    }

    private void RefreshProperties()
    {
        if (_dropdown == null || !_dropdown.IsAlive())
            return;

        _dropdown.Clear();
        var properties = GetTargetProperties();
        foreach (var name in properties)
            _dropdown.AddItem(name);
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

    private IEnumerable<string> GetTargetProperties()
    {
        var nodePath = GetEditedObjectField<NodePath>("_targetNode");
        if (nodePath == null || nodePath.IsEmpty)
            return Array.Empty<string>();

        var editedObject = GetEditedObject();
        if (editedObject is not Node binderNode)
            return Array.Empty<string>();

        var target = binderNode.GetNodeOrNull<Node>(nodePath);
        if (target == null)
            return Array.Empty<string>();

        var list = target.GetPropertyList();
        return list
            .Select(d => d["name"].AsString())
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .OrderBy(name => name);
    }
}
