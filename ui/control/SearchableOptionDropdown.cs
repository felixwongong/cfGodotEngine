using Godot;
using System;
using System.Collections.Generic;
using cfGodotEngine.Util;

namespace cfGodotEngine.UI;

[Tool]
public partial class SearchableOptionDropdown : Control
{
    #region Config Param

    public Vector2I dropdownSize
    {
        get => _dropdown.Size;
        set => _dropdown.Size = value;
    }

    public StyleBox dropDownStyle
    {
        get => _dropdown.GetThemeStylebox("normal");
        set => _dropdown.AddThemeStyleboxOverride("normal", value);
    }

    private string _placeholderText = "Select…";

    public string placeHolderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value;
            _searchFilter.PlaceholderText = value;
        }
    }

    #endregion
    
    public event Action<string, string> OnSelected; 

    private readonly Button _button;
    private readonly PopupPanel _dropdown;
    private readonly LineEdit _searchFilter;
    
    private readonly List<(string id, string displayText)> _items = new();
    private readonly ItemList _displayItems;
    private int selectingIndex = -1;

    public SearchableOptionDropdown() : base()
    {
        _button = new Button {
            Text = placeHolderText,
            AnchorsPreset = (int)LayoutPreset.FullRect,
        };
        AddChild(_button);
        
        _dropdown = new PopupPanel { Size = new Vector2I(400, 600) };
        var vb = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        vb.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("#1e1e1e"), BorderColor = new Color("#3e3e3e")});
        
        _searchFilter = new LineEdit { PlaceholderText = "Search…" };
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill};
        _displayItems = new ItemList { SelectMode = ItemList.SelectModeEnum.Single, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill};
        
        vb.AddChild(_searchFilter);
        scroll.AddChild(_displayItems);
        vb.AddChild(scroll);
        _dropdown.AddChild(vb);
        AddChild(_dropdown);

        _button.Pressed += ShowPopup; 
        _searchFilter.TextChanged += ApplyFilter;
        _searchFilter.GuiInput += e => {
            if (e is InputEventKey k)
            {
                if (k.Pressed && k.Keycode == Key.Escape) _dropdown.Hide();
                if (k.Pressed && k.Keycode == Key.Down) _displayItems.GrabFocus();
            }
        };
        
        _displayItems.ItemSelected += OnDisplayItemSelected;
        _displayItems.GuiInput += e => {
            if (e is InputEventKey k && k.Pressed)
            {
                if (k.Keycode == Key.Enter) OnDisplayItemSelected(_displayItems.GetSelectedItems()[0]);
                if (k.Keycode == Key.Escape) _dropdown.Hide();
            }
        };
    }

    public void ClearItems()
    {
        _items?.Clear();
        if(_displayItems.IsAlive()) _displayItems?.Clear();
    }
    
    public void Clear()
    {
        ClearItems();
        if(_button.IsAlive()) _button.Text = placeHolderText ?? "Select…";
    }

    public void AddItem(string id, string displayText = "")
    {
        if (string.IsNullOrWhiteSpace(displayText))
            displayText = id.ToLowerInvariant();
        
        _items.Add((id, displayText));
    }

    public void SetItems(IEnumerable<(string id, string displayText)> items)
    {
        Clear();
        foreach (var (id, text) in items) 
            AddItem(id, text);
        
        ApplyFilter(_searchFilter?.Text ?? "");
    }
    
    public void Select(string id)
    {
        Deselect();
        selectingIndex = _items.FindIndex(x => x.id.Equals(id, StringComparison.InvariantCultureIgnoreCase));
        if (selectingIndex != -1)
            _button.Text = _items[selectingIndex].displayText;
        else
        {
            GD.PrintErr($"OptionDropdown: Item with id '{id}' not found.");
            return;
        }
        ApplyFilter(_searchFilter.Text ?? "");
    }

    public void Deselect()
    {
        _displayItems.DeselectAll();
        selectingIndex = -1;
        _button.Text = placeHolderText;
    }

    private void OnDisplayItemSelected(long index)
    {
        var idx = (int)index;
        if (idx < 0 || idx >= _displayItems.ItemCount)
        {
            GD.PrintErr("OptionDropdown: Invalid list index selected.");
            return;
        }
        
        string id = (string)_displayItems.GetItemMetadata(idx);
        string displayText = _displayItems.GetItemText(idx);
        _button.Text = displayText;
        OnSelected?.Invoke(id, displayText);
        selectingIndex = _items.FindIndex(x => x.id.Equals(id, StringComparison.InvariantCultureIgnoreCase));
        _dropdown.Hide();
    }

    private void ShowPopup()
    {
        var mousePos = GetGlobalMousePosition();
        _dropdown.Popup(new Rect2I(new Vector2I((int)mousePos.X, (int)mousePos.Y), _dropdown.Size));
        _searchFilter.Text = "";
        ApplyFilter("");
        _searchFilter.GrabFocus();
    }

    private void ApplyFilter(string filter)
    {
        if(!_displayItems.IsAlive())
            return;
        
        _displayItems.Clear();
        
        filter = (filter ?? "").ToLowerInvariant();
        foreach (var (id, displayText) in _items)
        {
            if (filter.Length == 0 || id.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
            {
                int idx = _displayItems.ItemCount;
                _displayItems.AddItem(displayText);
                _displayItems.SetItemMetadata(idx, id);

                if (selectingIndex != -1)
                {
                    var selectedItem = _items[selectingIndex];
                    if (id.Equals(selectedItem.id, StringComparison.InvariantCultureIgnoreCase))
                    {
                        _displayItems.Select(idx);
                    }
                }
            }
        }
    }
}
