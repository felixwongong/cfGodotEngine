using System;
using System.Collections.Generic;
using cfGodotEngine.Binding;
using Godot;

namespace cfGodotEngine.UI;

/// <summary>
/// Lightweight Control base carrying the binding-source receiver contract.
/// UIPanel extends it (screens); collection item prefab roots use it directly
/// (rows) so entries delivered by CollectionBinding flow declaratively into
/// the subtree's Binding nodes.
/// </summary>
[GlobalClass]
public partial class UIComponent : Control, IBindingSource, ISourceReceiver
{
    protected IBindingSource _injectedSource;

    public static IEnumerable<string> BindingKeys => Array.Empty<string>();

    public void SetSource(IBindingSource source)
    {
        if (ReferenceEquals(_injectedSource, source))
            return;

        _injectedSource = source;
        BindingBase.RefreshDescendants(this);
    }

    public virtual IEnumerable<string> GetBindingKeys()
    {
        return _injectedSource?.GetBindingKeys() ?? BindingKeys;
    }

    public IPropertyMap GetBindings => Engine.IsEditorHint() ? null : _injectedSource?.GetBindings;

    public IDisposable BeginUpdate() => _injectedSource?.BeginUpdate();
}
