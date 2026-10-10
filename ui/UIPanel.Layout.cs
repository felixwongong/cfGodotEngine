#if TOOLS

using cfGodotEngine.SceneManagement;
using Godot;

namespace cfGodotEngine.UI;

[Tool]
public partial class UIPanel
{
    protected override void ConstructLayout(ActorBuilder t)
    {
        base.ConstructLayout(t);
        var layer = new CanvasLayer();
        t.Add("Layer", layer, "Layer");

        var root = new Control();
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepHeight);
        t.Add("Layer/PanelRoot", root, "PanelRoot");

        var safeRect = new SafeRect();
        safeRect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepHeight);
        t.Add("Layer/PanelRoot/SafeRect", safeRect, "SafeRect");

        var aspectRatio = new AspectRatioRoot();
        aspectRatio.MinRatio = 0.001f;
        aspectRatio.MaxRatio = 0.65f;
        aspectRatio.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect, Control.LayoutPresetMode.KeepHeight);
        t.Add("Layer/PanelRoot/SafeRect/AspectRatio", aspectRatio, "AspectRatio");
    }
}

#endif
