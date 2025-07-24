using cfEngine.Util;
using Godot;

namespace cfGodotEngine.Util;

public abstract partial class StateNode<TStateId, TState, TStateMachine>: Node
    where TStateMachine: StateMachineNode<TStateId, TState, TStateMachine> 
    where TState : StateNode<TStateId, TState, TStateMachine>
{
    public abstract TStateId Id { get; }
    protected TStateMachine stateMachine { get; private set; }

    [Export] private Node[] stateNodes;
    
    public void SetStateMachine(TStateMachine stateMachine)
    {
        this.stateMachine = stateMachine;
    }
    
    public virtual bool IsReady(StateParam param) => true;
    public virtual bool CanUpdate() => true;

    public new virtual void _Ready()
    {
    }

    public new virtual void _Process(double delta)
    {
    }

    internal void StartContext(StateParam param)
    {
        if (stateNodes != null)
        {
            foreach (var node in stateNodes)
            {
                node.ProcessMode = ProcessModeEnum.Inherit;
            }
        }
        _StartContext(param);
    }
        
    protected abstract void _StartContext(StateParam param);

    internal void OnEndContext()
    {
        _OnEndContext();
        if (stateNodes != null)
        {
            foreach (var node in stateNodes)
            {
                node.ProcessMode = ProcessModeEnum.Disabled;
            }
        }
    }
        
    protected virtual void _OnEndContext()
    {
    }
}