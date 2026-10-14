using System;
using cfEngine;
using Godot;

namespace cfGodotEngine.UI;

/// <summary>
/// Modal popup panel: a <see cref="UIPanel"/> whose skeleton carries its own full-rect dimmer and
/// content <see cref="CanvasLayer"/>. The dimmer's <see cref="Control.MouseFilterEnum.Stop"/> filter
/// consumes the press in the GUI phase (a gameplay input gate); when <c>_tapOutsideToClose</c> is
/// set it also raises <see cref="CloseRequested"/>. <see cref="PopupStack"/> owns the open stack,
/// the z-order (<c>BaseLayer + depth</c>) and the lifecycle.
/// <para>
/// The panel's inherited visibility sync (<see cref="UIPanel"/>) keeps the content
/// <see cref="CanvasLayer"/> hidden whenever <see cref="UIPanel.Hide"/> is called, so a hidden popup
/// never keeps rendering.
/// </para>
/// </summary>
[GlobalClass]
public partial class UIPopup : UIPanel
{
    private const string DimmerKey = "Dimmer";
    private const string UniqueKeyMeta = "unique_key";

    /// <summary>Per-popup opt-in for a dimmer tap closing the popup. StageResult keeps it false.</summary>
    [Export] private bool _tapOutsideToClose;

    /// <summary>Raised for a dimmer press when <c>_tapOutsideToClose</c> is enabled.</summary>
    public event Action<UIPopup> CloseRequested;

    private ColorRect _dimmer;

    public override void _Ready()
    {
        base._Ready();

        _dimmer = ResolveKeyed(DimmerKey) as ColorRect;
        if (_dimmer == null)
        {
            Log.LogWarning("[UIPopup] Dimmer not found; tap-outside-to-close and input gating are inactive.");
            return;
        }

        _dimmer.GuiInput += OnDimmerGuiInput;
    }

    public override void _ExitTree()
    {
        if (_dimmer != null && IsInstanceValid(_dimmer))
            _dimmer.GuiInput -= OnDimmerGuiInput;

        _dimmer = null;

        base._ExitTree();
    }

    /// <summary>
    /// Assigns the popup's z-order: its content layer is <c>BaseLayer + depth</c>, applied by
    /// <see cref="PopupStack"/> whenever the open stack changes.
    /// </summary>
    internal void SetStackDepth(int depth)
    {
        var layer = FindLayer(this);
        if (layer != null)
            layer.Layer = PopupStackCore.BaseLayer + depth;
    }

    private void OnDimmerGuiInput(InputEvent @event)
    {
        if (!_tapOutsideToClose)
            return;

        var pressed = @event switch
        {
            InputEventMouseButton mouseButton => mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left,
            InputEventScreenTouch screenTouch => screenTouch.Pressed,
            _ => false,
        };

        if (pressed)
            CloseRequested?.Invoke(this);
    }

    private Node ResolveKeyed(string key)
        => FindKeyed(this, key) ?? FindChild(key, recursive: true, owned: false);

    private static Node FindKeyed(Node root, string key)
    {
        foreach (var child in root.GetChildren(includeInternal: true))
        {
            if (child.HasMeta(UniqueKeyMeta) && child.GetMeta(UniqueKeyMeta).AsStringName() == key)
                return child;

            var found = FindKeyed(child, key);
            if (found != null)
                return found;
        }

        return null;
    }

    private static CanvasLayer FindLayer(Node root)
    {
        foreach (var child in root.GetChildren(includeInternal: true))
        {
            if (child is CanvasLayer layer)
                return layer;

            var found = FindLayer(child);
            if (found != null)
                return found;
        }

        return null;
    }
}
