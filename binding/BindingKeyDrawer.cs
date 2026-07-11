using System;
using System.Collections.Generic;
using System.Linq;
using cfGodotEngine.UI;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding
{
    [CustomPropertyDrawer]
    public partial class BindingKeyDrawer : CustomPropertyDrawer
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
                placeHolderText = "Select key...",
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
            foreach (var key in GetKeys())
                _dropdown.AddItem(key);
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
            if (editedObject is Binding binder)
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

        private IEnumerable<string> GetKeys()
        {
            var editedObject = GetEditedObject();
            if (editedObject is Binding binder)
                return binder.GetAvailableKeys();

            if (editedObject is CollectionBinding collectionBinder)
                return collectionBinder.GetAvailableKeys();

            return Array.Empty<string>();
        }
    }
}