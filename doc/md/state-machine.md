# State Machine

`StateMachineNode<TStateId, TState, TStateMachine>` provides a node-based implementation of a state machine. States derive from `StateNode<TStateId, TState, TStateMachine>` and can be added as children of the machine.

Transition using `TryGoToState` or `ForceGoToState` and subscribe to `BeforeStateChange` or `AfterStateChange` events when needed.
