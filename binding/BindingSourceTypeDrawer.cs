using System;
using System.Linq;
using cfGodotEngine.UI;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.Binding
{
    [CustomPropertyDrawer]
    public partial class BindingSourceTypeDrawer : CustomPropertyDrawer
    {
        private SearchableOptionDropdown _dropdown;

        protected override void _BuildNode(PropertyHint hintType, string hintString)
        {
            _dropdown = new SearchableOptionDropdown
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                placeHolderText = "Select binding source type...",
            };
            _dropdown.OnSelected += OnSelected;
            AddChild(_dropdown);

            RefreshTypes();
        }

        public override void _Ready()
        {
            base._Ready();
            RefreshSelection();
        }

        protected override void OnPropertyUpdated(Variant propertyValue)
        {
            if (!this.IsAlive())
                return;
            RefreshSelection();
        }

        protected override void _ClearNode()
        {
            if (_dropdown.IsAlive()) _dropdown.Clear();
        }

        private void RefreshTypes()
        {
            if (_dropdown == null || !_dropdown.IsAlive())
                return;

            _dropdown.Clear();
            foreach (var name in BindingSourceRegistryHost.TypeNames)
                _dropdown.AddItem(name);
        }

        private void RefreshSelection()
        {
            if (_dropdown == null || !_dropdown.IsAlive())
                return;

            var value = GetEditedPropertyValue();
            var id = value.VariantType == Variant.Type.Nil ? string.Empty : (string)value;
            _dropdown.SelectOrAdd(id, id);
        }

        private void OnSelected(string id, string displayName)
        {
            SetPropertyValue(new StringName(id));
        }
    }
}
