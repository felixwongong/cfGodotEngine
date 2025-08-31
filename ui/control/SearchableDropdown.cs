using Godot;
using System;
using System.Collections.Generic;

namespace cfGodotEngine.UI;

[Tool]
public partial class SearchableDropdown : Control
{
    private Vector2I _defaultSize = new Vector2I(400, 600);
    public Vector2I size
    {
        get => _popup?.Size ?? _defaultSize;
        set
        {
            if (_popup == null)
                _defaultSize = value;
            else 
                _popup.Size = value;
        }
    }

    public event Action<int, string> OnSelected; // (id, text)

    private readonly List<(int id, string text, string lower)> _items = new();
    private Button _button;
    private PopupPanel _popup;
    private LineEdit _search;
    private ItemList _list;
    
    public override void _Ready()
    {
        _button = new Button {
            Text = "Select…", 
            AnchorLeft = 0,
            AnchorTop = 0,
            AnchorRight = 1,
            AnchorBottom = 1,
        };
        
        _button.Pressed += ShowPopup;
        AddChild(_button);

        _popup = new PopupPanel { Size = _defaultSize };
        var bg = new StyleBoxFlat { BgColor = new Color("#1e1e1e"), BorderColor = new Color("#3e3e3e") };
        var vb = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        vb.AddThemeStyleboxOverride("normal", bg);

        _search = new LineEdit { PlaceholderText = "Search…" };
        _search.TextChanged += ApplyFilter;
        _search.GuiInput += e => {
            if (e is InputEventKey k)
            {
                if (k.Pressed && k.Keycode == Key.Escape) _popup.Hide();
                if (k.Pressed && k.Keycode == Key.Down) _list.GrabFocus();
            }
        };
        vb.AddChild(_search);

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill};
        _list = new ItemList { SelectMode = ItemList.SelectModeEnum.Single, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill};
        _list.ItemActivated += idx => Accept((int)idx); // Enter/double-click
        _list.ItemSelected += idx => { /* optional: preview */ };
        _list.GuiInput += e => {
            if (e is InputEventKey k && k.Pressed)
            {
                if (k.Keycode == Key.Enter) Accept(_list.GetSelectedItems()[0]);
                if (k.Keycode == Key.Escape) _popup.Hide();
            }
        };
        scroll.AddChild(_list);
        vb.AddChild(scroll);

        _popup.AddChild(vb);
        AddChild(_popup);
    }

    public void Clear()
    {
        _items.Clear();
        _list.Clear();
        _button.Text = "Select…";
    }

    public void AddItem(string text, int id)
    {
        _items.Add((id, text, text.ToLowerInvariant()));
    }

    public void SetItems(IEnumerable<(int id, string text)> items)
    {
        Clear();
        foreach (var (id, text) in items) AddItem(text, id);
        ApplyFilter(_search?.Text ?? "");
    }
    
    public void Accept(string text)
    {
        for (int i = 0; i < _list.ItemCount; i++)
        {
            if (_list.GetItemText(i) == text)
            {
                Accept(i);
                return;
            }
        }
    }

    public void Accept(int idx)
    {
        if (idx < 0 || idx >= _list.ItemCount) return;
        int id = (int)_list.GetItemMetadata(idx);
        string text = _list.GetItemText(idx);
        _button.Text = text;
        OnSelected?.Invoke(id, text);
        _popup.Hide();
    }

    private void ShowPopup()
    {
        var mousePos = GetGlobalMousePosition();
        _popup.Popup(new Rect2I(new Vector2I((int)mousePos.X, (int)mousePos.Y), _popup.Size));
        _search.Text = "";
        ApplyFilter("");
        _search.GrabFocus();
    }

    private void ApplyFilter(string filter)
    {
        _list.Clear();
        var f = (filter ?? "").ToLowerInvariant();
        foreach (var it in _items)
        {
            if (f.Length == 0 || it.lower.Contains(f))
            {
                int idx = _list.ItemCount;
                _list.AddItem(it.text);
                _list.SetItemMetadata(idx, it.id);
            }
        }
        if (_list.ItemCount > 0) _list.Select(0);
    }
}
