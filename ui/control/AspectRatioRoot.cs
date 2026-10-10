using Godot;

namespace cfGodotEngine.UI;

[Tool]
public partial class AspectRatioRoot : Godot.Control
{
    private float _minRatio = 0.001f;

    /// <summary>Minimum aspect ratio (e.g., 4:3 = 1.333)</summary>
    [Export]
    public float MinRatio
    {
        get => _minRatio;
        set
        {
            if (value <= 0.0f) value = 0.001f;
            if (value > _maxRatio) value = _maxRatio;
            _minRatio = value;
            UpdateLayout();
        }
    }

    private float _maxRatio = 0.65f;

    /// <summary>Maximum aspect ratio (e.g., 21:9 = 2.333)</summary>
    [Export]
    public float MaxRatio
    {
        get => _maxRatio;
        set
        {
            if (value < _minRatio) value = _minRatio;
            _maxRatio = value;
            UpdateLayout();
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        // Determine who dictates the available size (usually the viewport or a CanvasLayer)
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

    private Vector2 GetAvailableSize()
    {
        var parent = GetParent();

        // Get the size available to us (usually the full window)
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
            // Fallback for unexpected hierarchies
            return GetViewportRect().Size;
        }
    }

    private void UpdateLayout()
    {
        var availableSize = GetAvailableSize();

        if (availableSize.Y == 0.0f) return;

        float currentRatio = availableSize.X / availableSize.Y;
        var targetSize = availableSize;

        // Logic: Resize SELF to fit within ratios, but maximize otherwise
        if (currentRatio > _maxRatio)
        {
            // Parent is too wide (Ultrawide) -> Clamp our width
            targetSize.X = availableSize.Y * _maxRatio;
        }
        else if (currentRatio < _minRatio)
        {
            // Parent is too tall (Mobile) -> Clamp our height
            targetSize.Y = availableSize.X / _minRatio;
        }

        // This control is a centered/clamped container. Use equal opposite
        // anchors plus offsets instead of writing Size while stretch anchors
        // are active; Godot otherwise overwrites Size after _ready().
        AnchorLeft = 0.5f;
        AnchorTop = 0.5f;
        AnchorRight = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -targetSize.X / 2.0f;
        OffsetTop = -targetSize.Y / 2.0f;
        OffsetRight = targetSize.X / 2.0f;
        OffsetBottom = targetSize.Y / 2.0f;
    }

    public override void _Process(double delta)
    {
        // Keep editor in sync without needing to reload scene
        if (Engine.IsEditorHint())
        {
            UpdateLayout();
        }
    }

#if TOOLS
    [ExportToolButton("Print Current Aspect Ratio")]
    private Callable _printCurrentAspectRatioButton => new Callable(this, nameof(PrintCurrentAspectRatio));

    private void PrintCurrentAspectRatio()
    {
        var availableSize = GetAvailableSize();
        if (availableSize.Y == 0.0f)
        {
            GD.Print("AspectRatioRoot: parent height is zero; cannot compute aspect ratio.");
            return;
        }

        float currentRatio = availableSize.X / availableSize.Y;
        GD.Print($"AspectRatioRoot: current aspect ratio = {currentRatio:F4} ({availableSize.X}x{availableSize.Y})");
    }
#endif
}
