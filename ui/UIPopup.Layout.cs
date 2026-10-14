#if TOOLS

using cfGodotEngine.SceneManagement;
using Godot;

namespace cfGodotEngine.UI;

[Tool]
public partial class UIPopup
{
    /// <summary>
    /// Builds the popup skeleton. Deliberately does <b>not</b> call <c>base.ConstructLayout</c>: a
    /// popup is not the full-screen <see cref="UIPanel"/> AspectRatio tier — it is a centered,
    /// max-width-clamped box over its own dimmer.
    /// <code>
    /// Layer (CanvasLayer)
    /// └── Dimmer (ColorRect, full-rect, MouseFilter = Stop, translucent black)
    ///     └── SafeRect (full-rect)
    ///         └── Center (CenterContainer, full-rect)
    ///             └── PopupBox (PanelContainer, centered, width-clamped by anchor fractions)
    /// </code>
    /// </summary>
    protected override void ConstructLayout(ActorBuilder t)
    {
        var layer = new CanvasLayer();
        t.Add("Layer", layer, "Layer");

        var dimmer = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.5f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        dimmer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepHeight);
        t.Add("Layer/Dimmer", dimmer, "Dimmer");

        var safeRect = new SafeRect();
        safeRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepHeight);
        t.Add("Layer/Dimmer/SafeRect", safeRect, "SafeRect");

        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepHeight);
        t.Add("Layer/Dimmer/SafeRect/Center", center, "Center");

        // Width clamp by anchor fractions: no full-screen AspectRatio tier, no fixed-px floors.
        var popupBox = new PanelContainer
        {
            AnchorLeft = 0.1f,
            AnchorRight = 0.9f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
        };
        t.Add("Layer/Dimmer/SafeRect/Center/PopupBox", popupBox, "PopupBox");
    }
}

#endif
