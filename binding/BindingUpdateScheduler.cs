using System;
using System.Collections.Generic;
using Godot;

namespace cfGodotEngine.Binding;

/// <summary>
/// Coalesces binding change notifications to the end of the current frame.
/// Sources that call setters outside of an explicit <see cref="IBindingSource.BeginUpdate"/>
/// scope are automatically batched so each binder receives one dispatch per key per frame.
/// </summary>
public static class BindingUpdateScheduler
{
    private static readonly HashSet<Action> _pending = new();
    private static bool _subscribed;

    public static void Schedule(Action flush)
    {
        if (flush == null)
            return;

        _pending.Add(flush);
        if (_subscribed)
            return;

        if (Engine.GetMainLoop() is not SceneTree tree)
            return;

        _subscribed = true;
        tree.ProcessFrame += OnProcessFrame;
    }

    private static void OnProcessFrame()
    {
        if (Engine.GetMainLoop() is SceneTree tree)
            tree.ProcessFrame -= OnProcessFrame;

        _subscribed = false;

        if (_pending.Count == 0)
            return;

        var actions = new Action[_pending.Count];
        _pending.CopyTo(actions);
        _pending.Clear();

        foreach (var action in actions)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"BindingUpdateScheduler: flush failed: {ex}");
            }
        }
    }
}
