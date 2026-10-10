#if TOOLS
using System;
using System.Collections.Generic;
using cfGodotEngine.Binding;
using cfGodotEngine.Tool;
using Godot;

namespace cfGodotEngine.SceneManagement;

public abstract class ActorBuilder<TSelf> where TSelf : ActorBuilder<TSelf>
{
    public const string MetaUniqueKey = "unique_key";
    public const string MetaSnapshotExclude = "snapshot_exclude";

    private readonly BuilderState _state;
    private readonly string _path;

    public Node Node { get; }

    protected ActorBuilder(Node wrapped)
    {
        if (wrapped == null)
            GD.PushError("ActorBuilder: wrapped node is null");
        Node = wrapped;
        _state = new BuilderState();
    }

    protected ActorBuilder(Node wrapped, string path, BuilderState state)
    {
        Node = wrapped;
        _path = path;
        _state = state;
    }

    public TSelf Add(Node instance, string uniqueKey, string displayName = "", Action<TSelf> configure = null)
    {
        var child = RecordAdd(instance, uniqueKey, displayName, _path);
        if (child == null)
            return (TSelf)this;
        try
        {
            configure?.Invoke(child);
        }
        catch (Exception ex)
        {
            var path = _path == null ? uniqueKey : $"{_path}/{uniqueKey}";
            _state.RecordError($"ActorBuilder.Add: configure callback threw for '{path}': {ex.Message}");
        }
        return (TSelf)this;
    }

    public TSelf Add(string path, Node instance, string displayName = "")
    {
        if (string.IsNullOrEmpty(path))
        {
            _state.RecordError("ActorBuilder.Add: path is empty");
            return (TSelf)this;
        }

        var slash = path.LastIndexOf('/');
        var key = slash < 0 ? path : path[(slash + 1)..];
        if (key.Length == 0)
        {
            _state.RecordError($"ActorBuilder.Add: path '{path}' ends with '/'");
            return (TSelf)this;
        }

        var parentPath = slash < 0 ? null : path[..slash];
        if (parentPath != null && (parentPath.Length == 0 || !_state.Paths.ContainsKey(parentPath)))
        {
            _state.RecordError($"ActorBuilder.Add: parent path '{parentPath}' has not been added yet");
            return (TSelf)this;
        }

        var child = RecordAdd(instance, key, displayName, parentPath);
        return child ?? (TSelf)this;
    }

    public TSelf Meta(StringName key, Variant value)
    {
        if (Node == null)
        {
            GD.PushError("ActorBuilder.Meta: wrapped node is null");
            return (TSelf)this;
        }
        Node.SetMeta(key, value);
        return (TSelf)this;
    }

    public TSelf Bind(StringName key, StringName applyTo)
        => RecordBind(_path, key, applyTo);

    public TSelf Bind(string path, StringName key, StringName applyTo)
    {
        if (path == null || !_state.Paths.TryGetValue(path, out _))
        {
            _state.RecordError($"ActorBuilder.Bind: path '{path}' has not been added yet");
            return (TSelf)this;
        }
        return RecordBind(path, key, applyTo);
    }

    public TSelf States(Dictionary<string, StringName> map)
        => RecordStates(_path, map);

    public TSelf States(string path, Dictionary<string, StringName> map)
    {
        if (path == null || !_state.Paths.TryGetValue(path, out _))
        {
            _state.RecordError($"ActorBuilder.States: path '{path}' has not been added yet");
            return (TSelf)this;
        }
        return RecordStates(path, map);
    }

    public bool Build()
    {
        if (_state.Built)
        {
            GD.PushError("ActorBuilder.Build: already built");
            return false;
        }
        _state.Built = true;

        if (_state.Errors.Count > 0)
            return false;

        if (!Validate())
            return false;

        try
        {
            Apply();
        }
        catch (Exception ex)
        {
            GD.PushError($"ActorBuilder.Build: failed to apply layout: {ex}");
            return false;
        }

        return true;
    }

    private TSelf RecordAdd(Node instance, string uniqueKey, string displayName, string parentPath)
    {
        if (instance == null)
        {
            _state.RecordError("ActorBuilder.Add: instance is null");
            return null;
        }
        if (Node == null)
        {
            _state.RecordError("ActorBuilder.Add: wrapped node is null");
            return null;
        }
        if (string.IsNullOrEmpty(uniqueKey))
        {
            _state.RecordError("ActorBuilder.Add: uniqueKey is null or empty");
            return null;
        }
        if (uniqueKey.Contains('/'))
        {
            _state.RecordError($"ActorBuilder.Add: uniqueKey '{uniqueKey}' contains '/'");
            return null;
        }

        var path = parentPath == null ? uniqueKey : $"{parentPath}/{uniqueKey}";
        if (_state.Paths.ContainsKey(path))
        {
            _state.RecordError($"ActorBuilder.Add: path '{path}' already added");
            return null;
        }

        instance.SetMeta(MetaUniqueKey, new StringName(uniqueKey));
        if (!string.IsNullOrEmpty(displayName))
            instance.Name = displayName;

        var name = instance.Name;
        _state.Paths[path] = instance;
        _state.Ops.Add(new AddOp(path, parentPath, uniqueKey, name, instance));
        return Wrap(instance, path, _state);
    }

    private TSelf RecordBind(string targetPath, StringName key, StringName applyTo)
    {
        _state.Ops.Add(new BindOp(targetPath, key, applyTo));
        return (TSelf)this;
    }

    private TSelf RecordStates(string targetPath, Dictionary<string, StringName> map)
    {
        if (map == null)
        {
            _state.RecordError("ActorBuilder.States: map is null");
            return (TSelf)this;
        }
        _state.Ops.Add(new StatesOp(targetPath, map));
        return (TSelf)this;
    }

    private bool Validate()
    {
        var errors = new List<string>();
        var uniqueKeys = new HashSet<StringName>();

        foreach (var op in _state.Ops)
        {
            if (op is not AddOp add)
                continue;
            if (!uniqueKeys.Add(new StringName(add.UniqueKey)))
                errors.Add($"ActorBuilder.Build: duplicate unique key '{add.UniqueKey}'");
        }

        foreach (var op in _state.Ops)
        {
            switch (op)
            {
                case BindOp bind:
                {
                    var target = ResolveNode(bind.TargetPath);
                    var parentKey = GetUniqueKey(target);
                    var bindingKey = new StringName($"{parentKey}_bind_{bind.Key}");
                    if (!uniqueKeys.Add(bindingKey))
                        errors.Add($"ActorBuilder.Build: duplicate binding key '{bindingKey}'");
                    break;
                }
                case StatesOp states:
                {
                    var target = ResolveNode(states.TargetPath);
                    if (target is not Stateful2D)
                    {
                        errors.Add($"ActorBuilder.Build: node '{target?.Name}' is not a Stateful2D");
                        continue;
                    }
                    foreach (var childName in states.Map.Values)
                    {
                        if (!HasDeclaredChild(states.TargetPath, childName))
                            errors.Add($"ActorBuilder.Build: no declared child '{childName}' under '{states.TargetPath}'");
                    }
                    break;
                }
            }
        }

        if (errors.Count == 0)
            return true;

        foreach (var error in errors)
            GD.PushError(error);
        return false;
    }

    private void Apply()
    {
        foreach (var op in _state.Ops)
        {
            if (op is not AddOp add)
                continue;
            var parent = add.ParentPath == null ? Node : _state.Paths[add.ParentPath];
            parent.AddChild(add.Instance);
        }

        foreach (var op in _state.Ops)
        {
            if (op is not BindOp bind)
                continue;
            var target = ResolveNode(bind.TargetPath);
            BindOn(target, bind.Key, bind.ApplyTo);
        }

        foreach (var op in _state.Ops)
        {
            if (op is not StatesOp states)
                continue;
            var target = ResolveNode(states.TargetPath);
            StatesOn(target, states.Map);
        }
    }

    private void BindOn(Node target, StringName key, StringName applyTo)
    {
        var parentKey = GetUniqueKey(target);
        var binding = global::cfGodotEngine.Binding.Binding.Create(key, applyTo);
        binding.Name = "Binding";
        target.AddChild(binding);
        binding.SetMeta(MetaUniqueKey, new StringName($"{parentKey}_bind_{key}"));
    }

    private void StatesOn(Node target, Dictionary<string, StringName> map)
    {
        if (target is not Stateful2D stateful)
            throw new InvalidOperationException($"ActorBuilder.States: node '{target?.Name}' is not a Stateful2D");
        var resolved = new Godot.Collections.Dictionary<string, NodePath>();
        foreach (var (state, childName) in map)
        {
            var child = target.GetNodeOrNull<Node2D>(new NodePath(childName));
            if (child == null)
                throw new InvalidOperationException($"ActorBuilder.States: no child '{childName}' under '{target.Name}'");
            resolved[state] = new NodePath(childName);
        }
        stateful.SetStateMap(resolved);
    }

    private Node ResolveNode(string path)
        => path == null ? Node : _state.Paths[path];

    private static StringName GetUniqueKey(Node node)
        => node != null && node.HasMeta(MetaUniqueKey) ? node.GetMeta(MetaUniqueKey).AsStringName() : new StringName();

    private bool HasDeclaredChild(string parentPath, StringName childName)
    {
        var name = childName.ToString();
        foreach (var op in _state.Ops)
        {
            if (op is AddOp add && add.ParentPath == parentPath && add.Name == name)
                return true;
        }
        return false;
    }

    protected abstract TSelf Wrap(Node wrapped, string path, BuilderState state);

    protected sealed class BuilderState
    {
        public readonly Dictionary<string, Node> Paths = new();
        public readonly List<Op> Ops = new();
        public readonly List<string> Errors = new();
        public bool Built;

        public void RecordError(string message)
        {
            Errors.Add(message);
            GD.PushError(message);
        }
    }

    protected abstract class Op { }

    protected sealed class AddOp : Op
    {
        public string Path { get; }
        public string ParentPath { get; }
        public string UniqueKey { get; }
        public string Name { get; }
        public Node Instance { get; }

        public AddOp(string path, string parentPath, string uniqueKey, string name, Node instance)
        {
            Path = path;
            ParentPath = parentPath;
            UniqueKey = uniqueKey;
            Name = name;
            Instance = instance;
        }
    }

    protected sealed class BindOp : Op
    {
        public string TargetPath { get; }
        public StringName Key { get; }
        public StringName ApplyTo { get; }

        public BindOp(string targetPath, StringName key, StringName applyTo)
        {
            TargetPath = targetPath;
            Key = key;
            ApplyTo = applyTo;
        }
    }

    protected sealed class StatesOp : Op
    {
        public string TargetPath { get; }
        public Dictionary<string, StringName> Map { get; }

        public StatesOp(string targetPath, Dictionary<string, StringName> map)
        {
            TargetPath = targetPath;
            Map = map;
        }
    }
}

public class ActorBuilder : ActorBuilder<ActorBuilder>
{
    public ActorBuilder(Node wrapped) : base(wrapped) { }

    private ActorBuilder(Node wrapped, string path, BuilderState state) : base(wrapped, path, state) { }

    protected override ActorBuilder Wrap(Node wrapped, string path, BuilderState state)
        => new(wrapped, path, state);
}
#endif
