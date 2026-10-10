using Godot;

namespace cfGodotEngine.UI;

/// <summary>
/// Generic looping alpha pulse for a CanvasItem. State is supplied by a Binding
/// node through <see cref="Enabled"/>; this control has no domain knowledge.
/// </summary>
[GlobalClass]
public partial class PulseModulator : Node
{
    [Export]
    public NodePath TargetPath { get; set; } = new("..");

    [Export(PropertyHint.Range, "0.05,10.0,0.01")]
    public float PulsePeriod { get; set; } = 0.67f;

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    public float MinAlpha { get; set; } = 0.35f;

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    public float MaxAlpha { get; set; } = 0.85f;

    [Export(PropertyHint.Range, "0.0,1.0,0.01")]
    public float RestAlpha { get; set; } = 0.0f;

    [Export(PropertyHint.Range, "0.0,2.0,0.01")]
    public float FadeInDuration { get; set; } = 0.15f;

    [Export(PropertyHint.Range, "0.0,2.0,0.01")]
    public float FadeOutDuration { get; set; } = 0.2f;

    [Export]
    public bool HideAtRest { get; set; } = true;

    [Export]
    public Tween.TransitionType Transition { get; set; } = Tween.TransitionType.Sine;

    [Export]
    public Tween.EaseType Ease { get; set; } = Tween.EaseType.InOut;

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
            GD.PushWarning($"[{nameof(PulseModulator)}] Target '{TargetPath}' was not found.");
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
        if (_enabled)
            StartFadeIn();
        else
            StartFadeOut();
    }

    private void StartFadeIn()
    {
        KillTween();
        _target.Visible = true;

        if (FadeInDuration <= 0.0f)
        {
            SetAlpha(MaxAlpha);
            StartPulseLoop();
            return;
        }

        _tween = CreateTween();
        ConfigureTween(_tween);
        _tween.TweenProperty(_target, "modulate:a", MaxAlpha, FadeInDuration);
        _tween.TweenCallback(Callable.From(() =>
        {
            if (_enabled)
                StartPulseLoop();
        }));
    }

    private void StartFadeOut()
    {
        KillTween();

        if (!_ready || _target == null)
            return;

        if (FadeOutDuration <= 0.0f)
        {
            SetAlpha(RestAlpha);
            HideIfNeeded();
            return;
        }

        _tween = CreateTween();
        ConfigureTween(_tween);
        _tween.TweenProperty(_target, "modulate:a", RestAlpha, FadeOutDuration);
        _tween.TweenCallback(Callable.From(HideIfNeeded));
    }

    private void StartPulseLoop()
    {
        KillTween();

        if (!_enabled || _target == null)
            return;

        var halfPeriod = Mathf.Max(PulsePeriod * 0.5f, 0.025f);
        _tween = CreateTween();
        ConfigureTween(_tween);
        _tween.TweenProperty(_target, "modulate:a", MinAlpha, halfPeriod);
        _tween.TweenProperty(_target, "modulate:a", MaxAlpha, halfPeriod);
        _tween.SetLoops();
    }

    private void ConfigureTween(Tween tween)
    {
        tween.SetTrans(Transition);
        tween.SetEase(Ease);
    }

    private void HideIfNeeded()
    {
        if (!_enabled && HideAtRest && RestAlpha <= 0.001f && _target != null)
            _target.Visible = false;
    }

    private void SetAlpha(float alpha)
    {
        var color = _target.Modulate;
        color.A = Mathf.Clamp(alpha, 0.0f, 1.0f);
        _target.Modulate = color;
    }

    private void KillTween()
    {
        if (_tween == null)
            return;

        _tween.Kill();
        _tween = null;
    }
}
