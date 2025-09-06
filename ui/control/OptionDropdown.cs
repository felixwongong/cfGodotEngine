using Godot;
using System;
using System.Collections.Generic;
using cfGodotEngine.Util;

namespace cfGodotEngine.UI;

[Tool]
public partial class OptionDropdown : Control
{
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

    private string _placeholderText;

    public string placeHolderText
    {
        get => _placeholderText;
        set
        {
            _placeholderText = value;
            _searchFilter.PlaceholderText = value;
        }
    }
    
    public event Action<string, string> OnSelected; 

    private readonly List<(string id, string displayText)> _items = new();
    private readonly Button _button;
    private readonly PopupPanel _dropdown;
    private readonly LineEdit _searchFilter;
    private readonly ItemList _list;

    private string selecting;

    public OptionDropdown() : base()
    {
        _button = new Button {
            Text = "Select…", 
            AnchorLeft = 0,
            AnchorTop = 0,
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        AddChild(_button);
        
        _dropdown = new PopupPanel { Size = new Vector2I(400, 600) };
        var vb = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        vb.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("#1e1e1e"), BorderColor = new Color("#3e3e3e")});
        
        _searchFilter = new LineEdit { PlaceholderText = "Search…" };
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill};
        _list = new ItemList { SelectMode = ItemList.SelectModeEnum.Single, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill};
        
        vb.AddChild(_searchFilter);
        scroll.AddChild(_list);
        vb.AddChild(scroll);
        _dropdown.AddChild(vb);
        AddChild(_dropdown);

        _button.Pressed += ShowPopup; 
        _searchFilter.TextChanged += ApplyFilter;
        _searchFilter.GuiInput += e => {
            if (e is InputEventKey k)
            {
                if (k.Pressed && k.Keycode == Key.Escape) _dropdown.Hide();
                if (k.Pressed && k.Keycode == Key.Down) _list.GrabFocus();
            }
        };
        
        _list.ItemSelected += OnListItemSelected; // Enter/double-click
        _list.GuiInput += e => {
            if (e is InputEventKey k && k.Pressed)
            {
                if (k.Keycode == Key.Enter) OnListItemSelected(_list.GetSelectedItems()[0]);
                if (k.Keycode == Key.Escape) _dropdown.Hide();
            }
        };
    }
    
    public void Clear()
    {
        _items?.Clear();
        if(_list.IsAlive()) _list?.Clear();
        if(_button.IsAlive()) _button.Text = "Select…";
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
        selecting = id;
        ApplyFilter(_searchFilter?.Text ?? "");
    }

    public void Deselect()
    {
        _list.DeselectAll();
        _button.Text = string.IsNullOrWhiteSpace(_placeholderText) ? "Select…" : _placeholderText;
        selecting = string.Empty;
    }

    private void OnListItemSelected(long listIndex)
    {
        var idx = (int)listIndex;
        if (idx < 0 || idx >= _list.ItemCount)
        {
            _button.Text = string.Empty;
            selecting = string.Empty;
            return;
        }
        
        string id = (string)_list.GetItemMetadata(idx);
        string text = _list.GetItemText(idx);
        _button.Text = text;
        selecting = id;
        OnSelected?.Invoke(id, text);
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
        if(!_list.IsAlive())
            return;
        
        _list.Clear();
        
        int selectingIdx = -1;
        filter = (filter ?? "").ToLowerInvariant();
        foreach (var (id, displayText) in _items)
        {
            if (filter.Length == 0 || id.Contains(filter, StringComparison.InvariantCultureIgnoreCase))
            {
                int idx = _list.ItemCount;
                _list.AddItem(displayText);
                _list.SetItemMetadata(idx, id);
                
                if (!string.IsNullOrEmpty(selecting) && id.Equals(selecting, StringComparison.InvariantCultureIgnoreCase)) 
                    selectingIdx = idx;
            }
        }

        if (_list.ItemCount > 0 && selectingIdx != -1)
        {
            _list.Select(selectingIdx);
        }
    }
}
