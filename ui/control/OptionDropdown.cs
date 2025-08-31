using Godot;
using System;
using System.Collections.Generic;

namespace cfGodotEngine.UI;

[Tool]
public partial class OptionDropdown : Control
{
    public Vector2I dropdownSize
    {
        get => dropdown.Size;
        set => dropdown.Size = value;
    }

    public StyleBox dropDownStyle
    {
        get => dropdown.GetThemeStylebox("normal");
        set => dropdown.AddThemeStyleboxOverride("normal", value);
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
    private readonly PopupPanel dropdown;
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
        
        dropdown = new PopupPanel { Size = new Vector2I(400, 600) };
        var vb = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        vb.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("#1e1e1e"), BorderColor = new Color("#3e3e3e")});
        
        _searchFilter = new LineEdit { PlaceholderText = "Search…" };
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill};
        _list = new ItemList { SelectMode = ItemList.SelectModeEnum.Single, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill};
        
        vb.AddChild(_searchFilter);
        scroll.AddChild(_list);
        vb.AddChild(scroll);
        dropdown.AddChild(vb);
        AddChild(dropdown);

        _button.Pressed += ShowPopup; 
        _searchFilter.TextChanged += ApplyFilter;
        _searchFilter.GuiInput += e => {
            if (e is InputEventKey k)
            {
                if (k.Pressed && k.Keycode == Key.Escape) dropdown.Hide();
                if (k.Pressed && k.Keycode == Key.Down) _list.GrabFocus();
            }
        };
        //_list.ItemActivated += 
        _list.ItemSelected += OnListItemSelected; // Enter/double-click
        _list.GuiInput += e => {
            if (e is InputEventKey k && k.Pressed)
            {
                if (k.Keycode == Key.Enter) OnListItemSelected(_list.GetSelectedItems()[0]);
                if (k.Keycode == Key.Escape) dropdown.Hide();
            }
        };
    }
    
    public void Clear()
    {
        _items.Clear();
        _list.Clear();
        _button.Text = "Select…";
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

    private void OnListItemSelected(long listIndex)
    {
        var idx = (int)listIndex;
        if (idx < 0 || idx >= _list.ItemCount)
        {
            GD.PrintErr("Overflow index selected: ", idx);
            return;
        }
        
        string id = (string)_list.GetItemMetadata(idx);
        string text = _list.GetItemText(idx);
        _button.Text = text;
        selecting = id;
        OnSelected?.Invoke(id, text);
        dropdown.Hide();
    }

    private void ShowPopup()
    {
        var mousePos = GetGlobalMousePosition();
        dropdown.Popup(new Rect2I(new Vector2I((int)mousePos.X, (int)mousePos.Y), dropdown.Size));
        _searchFilter.Text = "";
        ApplyFilter("");
        _searchFilter.GrabFocus();
    }

    private void ApplyFilter(string filter)
    {
        _list.Clear();
        
        int selectingIdx = -1;
        var f = (filter ?? "").ToLowerInvariant();
        foreach (var it in _items)
        {
            if (f.Length == 0 || it.id.ToLowerInvariant().Contains(f))
            {
                int idx = _list.ItemCount;
                _list.AddItem(it.displayText);
                _list.SetItemMetadata(idx, it.id);
                
                if (!string.IsNullOrEmpty(selecting) && it.id == selecting) selectingIdx = idx;
            }
        }

        if (_list.ItemCount > 0)
        {
            selectingIdx = selectingIdx >= 0 ? selectingIdx : 0;
            _list.Select(selectingIdx);
            OnListItemSelected(selectingIdx);
        }
    }
}
