using System;
using System.Collections.Generic;
using cfGodotEngine.Binding;
using Godot;

namespace cfGodotEngine.UI;

[GlobalClass]
[CustomInspector]
public partial class UIPanel : UIComponent
{
    [Export(PropertyHint.None, "BindingSourceType")]
    private string _sourceType;

    [Export]
    private NodePath _runtimeSourcePath;

    public override IEnumerable<string> GetBindingKeys()
    {
        if (Engine.IsEditorHint())
            return string.IsNullOrEmpty(_sourceType)
                ? Array.Empty<string>()
                : BindingSourceRegistryHost.GetKeys(_sourceType);

        return _injectedSource?.GetBindingKeys()
               ?? BindingSourceRegistryHost.GetKeys(_sourceType);
    }

    public override void _Ready()
    {
        base._Ready();

        if (_runtimeSourcePath != null && !_runtimeSourcePath.IsEmpty)
        {
            var node = GetNodeOrNull<Node>(_runtimeSourcePath);
            if (node is IBindingSource source)
                SetSource(source);
        }
    }

    public new void Show()
    {
        base.Show();
    }

    public new void Hide()
    {
        base.Hide();
    }
}
