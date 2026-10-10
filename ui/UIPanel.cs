using System;
using System.Collections.Generic;
using cfGodotEngine.Binding;
using Godot;

namespace cfGodotEngine.UI;

/// <summary>
/// Screen-level <see cref="UIComponent"/> that owns the visibility of the
/// <see cref="CanvasLayer"/> content canvases that belong to it.
/// <para>
/// A panel's content normally lives under a child <see cref="CanvasLayer"/> (for example
/// <c>Layer</c>). Because <see cref="CanvasLayer"/> is a <see cref="Node"/> and not a
/// <see cref="CanvasItem"/>, hiding the panel's root <see cref="Control"/> does not suppress that
/// separate canvas, so a "hidden" screen keeps rendering. This class therefore synchronizes every
/// panel-owned descendant CanvasLayer from the panel's <b>effective</b> visibility — its own
/// <see cref="CanvasItem.Visible"/> combined with every ancestor <see cref="CanvasItem.Visible"/> up to
/// the tree root — on visibility notifications, on tree structure changes, and on
/// <c>_EnterTree</c>/<c>_Ready</c>. Ancestor <see cref="CanvasLayer"/> visibility is outside this
/// panel-owned gate (only ancestor <see cref="CanvasItem"/>s are consulted; see
/// <c>IsEffectivelyVisible</c>).
/// </para>
/// <para>
/// The gate is always recomputed from panel/ancestor state and never from a layer's previously
/// forced value, so <c>Hide()</c> can never latch a layer false after the panel is shown again.
/// Descendant Controls keep their own local <see cref="CanvasItem.Visible"/> (independently hidden
/// widgets stay hidden), and a nested <see cref="UIPanel"/> keeps ownership of its own canvases: an
/// enclosing panel only ever hands it the enclosing gate, and the nested panel's own hidden flag wins.
/// </para>
/// </summary>
[GlobalClass]
[CustomInspector]
public partial class UIPanel : UIComponent
{
    [Export(PropertyHint.None, "BindingSourceType")]
    private string _sourceType;

    [Export]
    private NodePath _runtimeSourcePath;

    private bool _syncingPanelCanvasLayers;
    private readonly List<CanvasItem> _ancestorCanvasItems = new();
    private SceneTree _subscribedTree;

    public override IEnumerable<string> GetBindingKeys()
    {
        if (Engine.IsEditorHint())
            return string.IsNullOrEmpty(_sourceType)
                ? Array.Empty<string>()
                : BindingSourceRegistryHost.GetKeys(_sourceType);

        return _injectedSource?.GetBindingKeys()
               ?? BindingSourceRegistryHost.GetKeys(_sourceType);
    }

    public override void _EnterTree()
    {
        base._EnterTree();

        SubscribeTree();
        RebuildAncestorSubscriptions();
        SyncPanelCanvasLayers(true);
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

        SubscribeTree();
        RebuildAncestorSubscriptions();
        SyncPanelCanvasLayers(true);
    }

    public override void _ExitTree()
    {
        UnsubscribeTree();
        ClearAncestorSubscriptions();

        base._ExitTree();
    }

    public override void _Notification(int what)
    {
        base._Notification(what);

        if (what == (int)NotificationVisibilityChanged)
            SyncPanelCanvasLayers(true);
    }

    public new void Show()
    {
        base.Show();
    }

    public new void Hide()
    {
        base.Hide();
    }

    /// <summary>
    /// Synchronizes this panel's owned descendant CanvasLayers with the panel's effective visibility.
    /// <paramref name="enclosingGate"/> is the already-computed gate of an enclosing
    /// <see cref="UIPanel"/> (always <c>true</c> for this panel's own state change), so an enclosing
    /// panel can never make a nested panel visible while the nested panel itself is hidden.
    /// Reentrant calls (a layer change that notifies a descendant) are ignored.
    /// </summary>
    private void SyncPanelCanvasLayers(bool enclosingGate)
    {
        if (_syncingPanelCanvasLayers || !IsInstanceValid(this))
            return;

        _syncingPanelCanvasLayers = true;
        try
        {
            var gate = enclosingGate && IsEffectivelyVisible();
            SynchronizeDescendantLayers(this, gate);
        }
        finally
        {
            _syncingPanelCanvasLayers = false;
        }
    }

    /// <summary>
    /// A panel renders only while it is inside the tree, locally visible, and no ancestor
    /// <see cref="CanvasItem"/> up to the tree root is locally hidden. The raw node parent chain is
    /// walked because a <see cref="CanvasLayer"/> boundary severs native
    /// <see cref="CanvasItem.IsVisibleInTree"/> propagation.
    /// <para>
    /// Ancestor <see cref="CanvasLayer"/> visibility is deliberately outside this panel-owned gate:
    /// only ancestor <see cref="CanvasItem"/>s are consulted, so an independently hidden enclosing
    /// <see cref="CanvasLayer"/> would not suppress this panel's own content layers. No such layout
    /// exists today (both screens are direct children of the plain <c>Node</c> <c>UIRoot</c>); a
    /// future layout that nests a panel under a hidden <see cref="CanvasLayer"/> must handle that
    /// layer's visibility itself.
    /// </para>
    /// </summary>
    private bool IsEffectivelyVisible()
    {
        if (!IsInsideTree() || !Visible)
            return false;

        for (var node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is CanvasItem canvasItem && !canvasItem.Visible)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Walks the panel-owned subtree and applies <paramref name="gate"/> to every descendant
    /// <see cref="CanvasLayer"/>, including layers nested below other layers or below ordinary Nodes.
    /// A nested <see cref="UIPanel"/> is delegated to instead of being traversed, so it can combine
    /// the enclosing gate with its own hidden flag. Descendant <see cref="CanvasItem.Visible"/> flags are
    /// deliberately left untouched.
    /// </summary>
    private static void SynchronizeDescendantLayers(Node parent, bool gate)
    {
        foreach (var child in parent.GetChildren(includeInternal: true))
        {
            if (!IsInstanceValid(child))
                continue;

            switch (child)
            {
                case UIPanel nestedPanel:
                    nestedPanel.SyncPanelCanvasLayers(gate);
                    break;
                case CanvasLayer layer:
                    if (layer.Visible != gate)
                        layer.Visible = gate;
                    SynchronizeDescendantLayers(layer, gate);
                    break;
                default:
                    SynchronizeDescendantLayers(child, gate);
                    break;
            }
        }
    }

    private void SubscribeTree()
    {
        var tree = GetTree();
        if (tree == null || tree == _subscribedTree)
            return;

        UnsubscribeTree();

        _subscribedTree = tree;
        _subscribedTree.NodeAdded += OnTreeNodeAdded;
        _subscribedTree.TreeChanged += OnTreeChanged;
    }

    private void UnsubscribeTree()
    {
        if (_subscribedTree != null && IsInstanceValid(_subscribedTree))
        {
            _subscribedTree.NodeAdded -= OnTreeNodeAdded;
            _subscribedTree.TreeChanged -= OnTreeChanged;
        }

        _subscribedTree = null;
    }

    private void OnTreeNodeAdded(Node node)
    {
        if (!IsInstanceValid(node) || !IsInsideTree())
            return;

        // Reconcile a layer added anywhere inside this panel's subtree synchronously, before the next
        // render, rather than waiting for the next explicit Show/Hide.
        for (var current = node.GetParent(); current != null; current = current.GetParent())
        {
            if (current == this)
            {
                SyncPanelCanvasLayers(true);
                return;
            }
        }
    }

    private void OnTreeChanged()
    {
        if (!IsInsideTree())
            return;

        // Reparenting can change this panel's ancestry and add/remove layers anywhere below it, so
        // rebuild the ancestor signal subscriptions before reconciling.
        RebuildAncestorSubscriptions();
        SyncPanelCanvasLayers(true);
    }

    private void RebuildAncestorSubscriptions()
    {
        ClearAncestorSubscriptions();

        if (!IsInsideTree())
            return;

        for (var node = GetParent(); node != null; node = node.GetParent())
        {
            if (node is CanvasItem canvasItem)
            {
                canvasItem.VisibilityChanged += OnAncestorVisibilityChanged;
                _ancestorCanvasItems.Add(canvasItem);
            }
        }
    }

    private void ClearAncestorSubscriptions()
    {
        foreach (var canvasItem in _ancestorCanvasItems)
        {
            if (IsInstanceValid(canvasItem))
                canvasItem.VisibilityChanged -= OnAncestorVisibilityChanged;
        }

        _ancestorCanvasItems.Clear();
    }

    private void OnAncestorVisibilityChanged()
    {
        SyncPanelCanvasLayers(true);
    }
}
