using Godot;

namespace cfGodotEngine.UI;

/// <summary>
/// Keeps panel content inside the device safe area (notch / home indicator /
/// rounded corners). Inset mode moves itself via anchors/offsets; ScaleToFit and
/// Hybrid scale direct Control children. All nontrivial math lives in
/// <see cref="SafeRectMath"/> so it is unit-testable without the Godot runtime.
/// </summary>
[Tool]
public partial class SafeRect : Godot.Control
{
    private SafeMode _mode = SafeMode.Inset;

    /// <summary>How content is kept inside the safe area.</summary>
    [Export]
    public SafeMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            UpdateLayout();
        }
    }

    private bool _applyTop = true;

    [Export]
    public bool ApplyTop
    {
        get => _applyTop;
        set
        {
            if (_applyTop == value) return;
            _applyTop = value;
            UpdateLayout();
        }
    }

    private bool _applyBottom = true;

    [Export]
    public bool ApplyBottom
    {
        get => _applyBottom;
        set
        {
            if (_applyBottom == value) return;
            _applyBottom = value;
            UpdateLayout();
        }
    }

    private bool _applyLeft = true;

    [Export]
    public bool ApplyLeft
    {
        get => _applyLeft;
        set
        {
            if (_applyLeft == value) return;
            _applyLeft = value;
            UpdateLayout();
        }
    }

    private bool _applyRight = true;

    [Export]
    public bool ApplyRight
    {
        get => _applyRight;
        set
        {
            if (_applyRight == value) return;
            _applyRight = value;
            UpdateLayout();
        }
    }

    private bool _debugSimulate = false;

    /// <summary>When true, use <see cref="DebugSimulateInsets"/> instead of the real device values.</summary>
    [Export]
    public bool DebugSimulate
    {
        get => _debugSimulate;
        set
        {
            if (_debugSimulate == value) return;
            _debugSimulate = value;
            UpdateLayout();
        }
    }

    private Vector4 _debugSimulateInsets = Vector4.Zero;

    /// <summary>x=Left, y=Top, z=Right, w=Bottom in design units.</summary>
    [Export]
    public Vector4 DebugSimulateInsets
    {
        get => _debugSimulateInsets;
        set
        {
            if (_debugSimulateInsets == value) return;
            _debugSimulateInsets = value;
            UpdateLayout();
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();

        var parent = GetParent();
        if (parent is Godot.Control parentControl)
        {
            parentControl.Resized += UpdateLayout;
        }
        else if (parent is SubViewport parentSubViewport)
        {
            parentSubViewport.SizeChanged += UpdateLayout;
        }

        UpdateLayout();
    }

    public override void _ExitTree()
    {
        var parent = GetParent();
        if (parent is Godot.Control parentControl)
        {
            parentControl.Resized -= UpdateLayout;
        }
        else if (parent is SubViewport parentSubViewport)
        {
            parentSubViewport.SizeChanged -= UpdateLayout;
        }

        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        // Keep the editor preview in sync without needing to reload the scene.
        if (Engine.IsEditorHint())
        {
            UpdateLayout();
        }
    }

    private Vector2 GetAvailableSize()
    {
        var parent = GetParent();

        if (parent is Godot.Control parentControl)
        {
            return parentControl.Size;
        }
        else if (parent is SubViewport parentSubViewport)
        {
            return parentSubViewport.Size;
        }
        else
        {
            return GetViewportRect().Size;
        }
    }

    private float GetContentScale()
    {
        var window = GetWindow();
        float f = window?.ContentScaleFactor ?? 1f;
        return f <= 0f ? 1f : f;
    }

    private void UpdateLayout()
    {
        var parentSize = GetAvailableSize();
        if (parentSize.Y == 0f) return;

        SafeRectMath.Insets raw = DebugSimulate
            ? SafeRectMath.Insets.FromLtrb(
                DebugSimulateInsets.X,
                DebugSimulateInsets.Y,
                DebugSimulateInsets.Z,
                DebugSimulateInsets.W)
            : SafeRectMath.FromScreen(
                DisplayServer.GetDisplaySafeArea(),
                DisplayServer.ScreenGetSize(),
                GetContentScale());

        var masked = SafeRectMath.Mask(raw, ApplyTop, ApplyBottom, ApplyLeft, ApplyRight);
        var plan = SafeRectMath.LayoutPlan.For(Mode, parentSize, masked);

        // Write anchors + offsets, never Size, while stretch anchors are active.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        OffsetLeft = plan.SelfOffsets.Left;
        OffsetTop = plan.SelfOffsets.Top;
        OffsetRight = plan.SelfOffsets.Right;
        OffsetBottom = plan.SelfOffsets.Bottom;

        // Center a (typically center-anchored) child before scaling so the scale
        // pivots about its middle without overwriting an anchor-driven Position.
        foreach (Node child in GetChildren())
        {
            if (child is Control c)
            {
                c.PivotOffset = c.Size * 0.5f;
                c.Scale = new Vector2(plan.ChildScale, plan.ChildScale);
            }
        }
    }
}
