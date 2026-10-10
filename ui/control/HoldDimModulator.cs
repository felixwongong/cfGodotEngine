using Godot;

namespace cfGodotEngine.UI;

/// <summary>
/// Generic hold-dim for a CanvasItem: lowers alpha while enabled, restores on
/// disable. State is supplied by a Binding node through <see cref="Enabled"/>;
/// this control has no domain knowledge. Writes self_modulate:a so it never
/// conflicts with modulate-level tint bindings on the same node.
/// </summary>
[GlobalClass]
public partial class HoldDimModulator : Node
{
    [Export]
    public NodePath TargetPath { get; set; } = new("..");

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    public float DimAlpha { get; set; } = 0.4f;

    [Export(PropertyHint.Range, "0.0,2.0,0.01")]
    public float FadeInDuration { get; set; } = 0.05f;

    [Export(PropertyHint.Range, "0.0,2.0,0.01")]
    public float FadeOutDuration { get; set; } = 0.1f;

    private CanvasItem _target;
    private Tween _tween;
    private bool _enabled;
    private bool _ready;

    [Export]
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;

            _enabled = value;
            if (_ready)
                ApplyEnabled();
        }
    }

    public override void _Ready()
    {
        _target = GetNodeOrNull<CanvasItem>(TargetPath);
        if (_target == null)
        {
            GD.PushWarning($"[{nameof(HoldDimModulator)}] Target '{TargetPath}' was not found.");
            return;
        }

        _ready = true;
        ApplyEnabled();
    }

    public override void _ExitTree()
    {
        KillTween();
        _ready = false;
        base._ExitTree();
    }

    private void ApplyEnabled()
    {
        TweenAlpha(_enabled ? DimAlpha : 1f, _enabled ? FadeInDuration : FadeOutDuration);
    }

    private void TweenAlpha(float alpha, float duration)
    {
        KillTween();
        if (_target == null)
            return;

        if (duration <= 0.0f)
        {
            SetAlpha(alpha);
            return;
        }

        _tween = CreateTween();
        _tween.TweenProperty(_target, "self_modulate:a", alpha, duration);
    }

    private void SetAlpha(float alpha)
    {
        var color = _target.SelfModulate;
        color.A = Mathf.Clamp(alpha, 0.0f, 1.0f);
        _target.SelfModulate = color;
    }

    private void KillTween()
    {
        if (_tween == null)
            return;

        _tween.Kill();
        _tween = null;
    }
}
