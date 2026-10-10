using Godot;

namespace cfGodotEngine.UI;

/// <summary>
/// Generic one-shot scale punch. Bind a boolean to <see cref="PlayIfTrue"/>
/// when an effect should run only on the true edge of a state value.
/// </summary>
[GlobalClass]
public partial class PopTweener : Node
{
    [Export]
    public NodePath TargetPath { get; set; } = new("..");

    [Export(PropertyHint.Range, "1.0,2.0,0.01")]
    public float PeakScale { get; set; } = 1.15f;

    [Export(PropertyHint.Range, "0.05,2.0,0.01")]
    public float Duration { get; set; } = 0.25f;

    [Export]
    public Tween.TransitionType Transition { get; set; } = Tween.TransitionType.Back;

    [Export]
    public Tween.EaseType Ease { get; set; } = Tween.EaseType.Out;

    private Control _target;
    private Tween _tween;
    private Vector2 _baseScale = Vector2.One;
    private bool _active;

    public override void _Ready()
    {
        _target = GetNodeOrNull<Control>(TargetPath);
        if (_target == null)
        {
            GD.PushWarning($"[{nameof(PopTweener)}] Target '{TargetPath}' was not found.");
            return;
        }

        _baseScale = _target.Scale;
        _target.Resized += UpdatePivot;
        UpdatePivot();
    }

    public override void _ExitTree()
    {
        if (_target != null)
            _target.Resized -= UpdatePivot;

        KillTween();
        base._ExitTree();
    }

    public void PlayIfTrue(bool active)
    {
        if (!active)
        {
            _active = false;
            return;
        }

        if (_active || _target == null)
            return;

        _active = true;
        KillTween();
        _target.Scale = _baseScale;

        var peak = _baseScale * PeakScale;
        var firstHalf = Mathf.Max(Duration * 0.4f, 0.025f);
        var secondHalf = Mathf.Max(Duration - firstHalf, 0.025f);

        _tween = CreateTween();
        _tween.SetTrans(Transition);
        _tween.SetEase(Ease);
        _tween.TweenProperty(_target, "scale", peak, firstHalf);
        _tween.TweenProperty(_target, "scale", _baseScale, secondHalf);
    }

    private void UpdatePivot()
    {
        if (_target != null)
            _target.PivotOffset = _target.Size * 0.5f;
    }

    private void KillTween()
    {
        if (_tween == null)
            return;

        _tween.Kill();
        _tween = null;
    }
}
