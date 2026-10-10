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

public partial class UIRoot: Node, IService
{
    public enum LoadOption
    {
        Lazy,
        Eager
    }

    private class Registration
    {
        public IBindingSource view;
        public string resourcePath;
        public UIPanel panel;
    }

    private readonly Dictionary<Type, Registration> _registrations = new();

    public static UIRoot CreateInScene()
    {
        var ui = new UIRoot();
        ui.Name = nameof(UIRoot);
        NodeUtil.GetSceneTree().Root.AddChild(ui);
        return ui;
    }

    public Task Register<TView>(TView view, string resourcePath, LoadOption option = LoadOption.Lazy)
        where TView : class, IBindingSource
    {
        if (view == null)
            return Task.FromException(new ArgumentNullException(nameof(view)));

        var viewType = typeof(TView);
        if (_registrations.ContainsKey(viewType))
            Log.LogWarning($"[UIRoot] View type {viewType.Name} is already registered, replacing.");

        var registration = new Registration
        {
            view = view,
            resourcePath = resourcePath
        };
        _registrations[viewType] = registration;

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

    public UIPanel GetPanel<TView>() where TView : class, IBindingSource
    {
        return _registrations.TryGetValue(typeof(TView), out var registration)
            ? registration.panel
            : null;
    }

    public Task<UIPanel> Show<TView>() where TView : class, IBindingSource
    {
        if (!_registrations.TryGetValue(typeof(TView), out var registration))
        {
            var ex = new KeyNotFoundException($"[UIRoot] View type {typeof(TView).Name} is not registered.");
            Log.LogException(ex);
            return Task.FromException<UIPanel>(ex);
        }

        var loadTask = registration.panel != null ? Task.FromResult(registration.panel) : LoadPanel(registration);
        return loadTask.ContinueWithSynchronized(t =>
        {
            if (t.IsFaulted || t.Result == null)
                return null;

            t.Result.Show();
            return t.Result;
        });
    }

    public void Hide<TView>() where TView : class, IBindingSource
    {
        if (_registrations.TryGetValue(typeof(TView), out var registration))
            registration.panel?.Hide();
    }

    private Task<UIPanel> LoadPanel(Registration registration)
    {
        try
        {
            var getManager = Domain.Current.Get<AssetManager<Resource>>();
            if (getManager.HasError(out var error))
            {
                Log.LogException(error);
                return Task.FromException<UIPanel>(error);
            }

            var packedScene = getManager.value.Load<PackedScene>(registration.resourcePath);
            if (packedScene == null)
            {
                var ex = new Exception($"[UIRoot] Failed to load panel scene: {registration.resourcePath}");
                Log.LogException(ex);
                return Task.FromException<UIPanel>(ex);
            }

            var panel = packedScene.Instantiate<UIPanel>();
            panel.SetSource(registration.view);
            AddChild(panel);
            panel.Hide();
            registration.panel = panel;
            return Task.FromResult(panel);
        }
        catch (Exception ex)
        {
            Log.LogException(ex);
            return Task.FromException<UIPanel>(ex);
        }
    }
}
