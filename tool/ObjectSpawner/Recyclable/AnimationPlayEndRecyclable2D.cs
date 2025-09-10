using Godot;

namespace cfGodotEngine.Tool;

[Tool]
[GlobalClass]
public partial class AnimationPlayEndRecyclable2D: SimpleRecyclable2D
{
    [Export] private AnimationPlayer _animationPlayer;
    [Export] private string _animationName;

#if TOOLS
    public override string[] _GetConfigurationWarnings()
    {
        if (_animationPlayer == null)
            return ["Requires an AnimationPlayer node."];
        
        if (string.IsNullOrEmpty(_animationName))
            return ["animationName is not set."];
        
        if (!_animationPlayer.HasAnimation(_animationName))
            return [$"AnimationPlayer does not have animation '{_animationName}'."];
        
        return [];
    }
#endif
    
    public override void _Ready()
    {
        base._Ready();
        if (_animationPlayer == null)
            GD.PrintErr("PlayAnimationRecyclable2D: AnimationPlayer node not found.");
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        _animationPlayer.AnimationFinished += OnAnimationFinished;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        _animationPlayer.AnimationFinished -= OnAnimationFinished;
    }

    public override void Initialize()
    {
        base.Initialize();
        _animationPlayer.Play(_animationName);
    }

    public override void Clear()
    {
        base.Clear();
        if (_animationPlayer == null)
            return;
        
        _animationPlayer.Stop();
    }

    private void OnAnimationFinished(StringName animName)
    {
        if (animName == _animationName)
            Recycle();
    }
}