using System;
using System.Collections.Generic;
using cfEngine;

namespace cfGodotEngine.UI;

/// <summary>
/// Engine-free bookkeeping half of <see cref="PopupStack"/>: the view-type → resource-path
/// registration map and the ordered bottom→top open stack (depth == index). It holds no Godot
/// references, so the stack/z-order rules are unit-testable without the runtime (precedent:
/// <c>CommandConsoleCore</c>, the pure halves of <c>DebugUiScale</c>).
/// <para>
/// Mirrors <see cref="UIRoot"/>'s registry semantics — a re-register replaces the path and warns
/// — while adding the ordered open stack that gives each popup its z-order. All invalid
/// operations are no-ops that log a warning rather than throwing, so a caller can never
/// soft-lock a screen by mis-closing a popup.
/// </para>
/// </summary>
public sealed class PopupStackCore
{
    /// <summary>Base CanvasLayer used by the bottom-most open popup (depth 0).</summary>
    public const int BaseLayer = 100;

    private readonly Dictionary<Type, string> _registrations = new();
    private readonly List<Type> _stack = new();

    /// <summary>The CanvasLayer for a popup at <paramref name="depth"/> (bottom = 0).</summary>
    public int LayerForDepth(int depth) => BaseLayer + depth;

    /// <summary>True while at least one popup is open.</summary>
    public bool AnyOpen => _stack.Count > 0;

    /// <summary>Number of currently open popups.</summary>
    public int OpenCount => _stack.Count;

    /// <summary>The open stack, bottom→top; depth == index.</summary>
    public IReadOnlyList<Type> Stack => _stack;

    /// <summary>The registration map, view type → resource path.</summary>
    public IReadOnlyDictionary<Type, string> Registrations => _registrations;

    /// <summary>True when <paramref name="viewType"/> has been registered.</summary>
    public bool IsRegistered(Type viewType) => viewType != null && _registrations.ContainsKey(viewType);

    /// <summary>The registered resource path for <paramref name="viewType"/>, or null.</summary>
    public string GetResourcePath(Type viewType)
        => viewType != null && _registrations.TryGetValue(viewType, out var path) ? path : null;

    /// <summary>
    /// Registers (or replaces) a view type. A re-register warns and overwrites the path,
    /// mirroring <see cref="UIRoot.Register{TView}"/>. Returns true when newly added.
    /// </summary>
    public bool Register(Type viewType, string resourcePath)
    {
        if (viewType == null)
            throw new ArgumentNullException(nameof(viewType));

        if (_registrations.ContainsKey(viewType))
        {
            Log.LogWarning($"[PopupStack] View type {viewType.Name} is already registered, replacing.");
            _registrations[viewType] = resourcePath;
            return false;
        }

        _registrations[viewType] = resourcePath;
        return true;
    }

    /// <summary>True when <paramref name="viewType"/> is open.</summary>
    public bool IsOpen(Type viewType) => viewType != null && _stack.Contains(viewType);

    /// <summary>Depth (index) of an open view type, or -1 when not open.</summary>
    public int DepthOf(Type viewType) => viewType == null ? -1 : _stack.IndexOf(viewType);

    /// <summary>
    /// Pushes <paramref name="viewType"/> onto the stack. An unregistered type or one already
    /// open is a no-op that warns and returns false.
    /// </summary>
    public bool Open(Type viewType)
    {
        if (viewType == null)
            throw new ArgumentNullException(nameof(viewType));

        if (!_registrations.ContainsKey(viewType))
        {
            Log.LogWarning($"[PopupStack] View type {viewType.Name} is not registered.");
            return false;
        }

        if (_stack.Contains(viewType))
        {
            Log.LogWarning($"[PopupStack] Popup {viewType.Name} is already open; open is a no-op.");
            return false;
        }

        _stack.Add(viewType);
        return true;
    }

    /// <summary>
    /// Pops the top of the stack and returns it, or null (with a warning) when empty.
    /// </summary>
    public Type CloseTop()
    {
        if (_stack.Count == 0)
        {
            Log.LogWarning("[PopupStack] CloseTop called with an empty stack.");
            return null;
        }

        var top = _stack[_stack.Count - 1];
        _stack.RemoveAt(_stack.Count - 1);
        return top;
    }

    /// <summary>
    /// Closes <paramref name="viewType"/> wherever it sits in the stack; survivors' depths are
    /// recomputed from their indices. Closing an unopened type is a no-op that warns.
    /// </summary>
    public bool Close(Type viewType)
    {
        if (viewType == null)
            throw new ArgumentNullException(nameof(viewType));

        var index = _stack.IndexOf(viewType);
        if (index < 0)
        {
            Log.LogWarning($"[PopupStack] Popup {viewType.Name} is not open; close is a no-op.");
            return false;
        }

        _stack.RemoveAt(index);
        return true;
    }

    /// <summary>Empties the open stack, returning how many popups were open.</summary>
    public int Clear()
    {
        var count = _stack.Count;
        _stack.Clear();
        return count;
    }
}
