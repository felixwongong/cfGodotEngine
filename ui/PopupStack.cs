using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using cfEngine;
using cfEngine.Asset;
using cfEngine.Core;
using cfEngine.Extension;
using cfEngine.Service;
using cfGodotEngine.Binding;
using cfGodotEngine.Util;
using Godot;

namespace cfGodotEngine.UI;

/// <summary>
/// Modal popup stack, sibling to <see cref="UIRoot"/>. Mirrors UIRoot's register/load/show
/// lifecycle (cached panels, hidden-not-freed, warn-and-replace re-register) but keeps an ordered
/// open stack (bottom→top) via <see cref="PopupStackCore"/> so each popup owns its dimmer and a
/// content <see cref="CanvasLayer"/> at <c>BaseLayer + depth</c>.
/// <para>
/// Reopening an already-open type is a no-op that returns the cached popup. Registered popups are
/// hidden, never freed. Scene loads attach on the Godot main thread
/// (<see cref="TaskExtension.ContinueWithSynchronized"/>), never a plain <c>ContinueWith</c>.
/// </para>
/// </summary>
public partial class PopupStack : Node, IService
{
    public enum LoadOption
    {
        Lazy,
        Eager
    }

    private sealed class Registration
    {
        public IBindingSource view;
        public string resourcePath;
        public UIPopup panel;
        public Action<UIPopup> closeHandler;
    }

    private readonly Dictionary<Type, Registration> _registrations = new();
    private readonly PopupStackCore _core = new();

    /// <summary>True while at least one popup is open.</summary>
    public bool AnyOpen => _core.AnyOpen;

    /// <summary>Number of currently open popups.</summary>
    public int OpenCount => _core.OpenCount;

    public static PopupStack CreateInScene()
    {
        var stack = new PopupStack { Name = nameof(PopupStack) };
        NodeUtil.GetSceneTree().Root.AddChild(stack);
        return stack;
    }

    public Task Register<TView>(TView view, string resourcePath, LoadOption option = LoadOption.Lazy)
        where TView : class, IBindingSource
    {
        if (view == null)
            return Task.FromException(new ArgumentNullException(nameof(view)));

        var viewType = typeof(TView);
        if (_registrations.ContainsKey(viewType))
            Log.LogWarning($"[PopupStack] View type {viewType.Name} is already registered, replacing.");

        var registration = new Registration
        {
            view = view,
            resourcePath = resourcePath
        };
        _registrations[viewType] = registration;
        _core.Register(viewType, resourcePath);

        if (option == LoadOption.Eager)
            return LoadPanel(registration);

        return Task.CompletedTask;
    }

    public TView Get<TView>() where TView : class, IBindingSource
    {
        return _registrations.TryGetValue(typeof(TView), out var registration)
            ? registration.view as TView
            : null;
    }

    /// <summary>True when the popup for <typeparamref name="TView"/> is currently open.</summary>
    public bool IsOpen<TView>() where TView : class, IBindingSource => _core.IsOpen(typeof(TView));

    /// <summary>True when the popup for <paramref name="viewType"/> is currently open.</summary>
    public bool IsOpen(Type viewType) => _core.IsOpen(viewType);

    /// <summary>
    /// Opens the registered popup: lazily loads (mirroring <see cref="UIRoot.Show{TView}"/>),
    /// pushes it onto the core stack, assigns its z-order, wires the close-request handler, and
    /// shows it. An already-open type returns the cached popup.
    /// </summary>
    public Task<UIPopup> Open<TView>() where TView : class, IBindingSource => Open(typeof(TView));

    public Task<UIPopup> Open(Type viewType)
    {
        if (!_registrations.TryGetValue(viewType, out var registration))
        {
            var ex = new KeyNotFoundException($"[PopupStack] View type {viewType.Name} is not registered.");
            Log.LogException(ex);
            return Task.FromException<UIPopup>(ex);
        }

        if (_core.IsOpen(viewType))
            return Task.FromResult(registration.panel);

        var loadTask = registration.panel != null ? Task.FromResult(registration.panel) : LoadPanel(registration);
        return loadTask.ContinueWithSynchronized(t =>
        {
            if (t.IsFaulted || t.Result == null)
                return null;

            var popup = t.Result;
            if (!_core.Open(viewType))
                return popup;

            if (registration.closeHandler != null)
                popup.CloseRequested -= registration.closeHandler;
            registration.closeHandler = _ => Close(viewType);
            popup.CloseRequested += registration.closeHandler;

            SyncDepths();
            popup.Show();
            return popup;
        });
    }

    public void Close<TView>() where TView : class, IBindingSource => Close(typeof(TView));

    public void Close(Type viewType)
    {
        if (!_core.Close(viewType))
            return;

        HideRegistration(viewType);
        SyncDepths();
    }

    public void CloseTop()
    {
        var top = _core.CloseTop();
        if (top == null)
            return;

        HideRegistration(top);
        SyncDepths();
    }

    /// <summary>
    /// Hides every open popup and empties the stack. Registrations and instantiated panels stay
    /// cached (UIRoot lifetime) so a later open reuses them.
    /// </summary>
    public void Clear()
    {
        var open = new List<Type>(_core.Stack);
        _core.Clear();
        foreach (var viewType in open)
            HideRegistration(viewType);
    }

    private void HideRegistration(Type viewType)
    {
        if (_registrations.TryGetValue(viewType, out var registration))
            HidePanel(registration);
    }

    private void HidePanel(Registration registration)
    {
        if (registration.panel == null)
            return;

        if (registration.closeHandler != null)
            registration.panel.CloseRequested -= registration.closeHandler;
        registration.panel.Hide();
    }

    /// <summary>Re-applies <c>BaseLayer + depth</c> to every open popup (depth == index).</summary>
    private void SyncDepths()
    {
        var stack = _core.Stack;
        for (var depth = 0; depth < stack.Count; depth++)
        {
            if (_registrations.TryGetValue(stack[depth], out var registration) && registration.panel != null)
                registration.panel.SetStackDepth(depth);
        }
    }

    private Task<UIPopup> LoadPanel(Registration registration)
    {
        try
        {
            var getManager = Domain.Current.Get<AssetManager<Resource>>();
            if (getManager.HasError(out var error))
            {
                Log.LogException(error);
                return Task.FromException<UIPopup>(error);
            }

            var packedScene = getManager.value.Load<PackedScene>(registration.resourcePath);
            if (packedScene == null)
            {
                var ex = new Exception($"[PopupStack] Failed to load popup scene: {registration.resourcePath}");
                Log.LogException(ex);
                return Task.FromException<UIPopup>(ex);
            }

            var popup = packedScene.Instantiate<UIPopup>();
            popup.SetSource(registration.view);
            AddChild(popup);
            popup.Hide();
            registration.panel = popup;
            return Task.FromResult(popup);
        }
        catch (Exception ex)
        {
            Log.LogException(ex);
            return Task.FromException<UIPopup>(ex);
        }
    }

#if TOOLS
    /// <summary>
    /// Test-only seam: registers a runtime-constructed popup under <paramref name="viewType"/> so the
    /// native probe can exercise the real stack mechanics without a scene file. Whole-file TOOLS-gated
    /// (the caller <c>PopupStackProbe</c> lives under <c>Scripts/**/Editor/**</c>), so it is absent from
    /// the exported assembly.
    /// </summary>
    internal void RegisterInstanceForProbe(Type viewType, string resourcePath, UIPopup popup)
    {
        if (viewType == null || popup == null)
            return;

        var registration = new Registration
        {
            view = null,
            resourcePath = resourcePath,
            panel = popup
        };
        _registrations[viewType] = registration;
        _core.Register(viewType, resourcePath);

        if (!popup.IsInsideTree())
            AddChild(popup);
        popup.Hide();
    }
#endif
}
