using cfEngine.DataStructure;
using Godot;

namespace cfGodotEngine.Binding;

[Tool]
[CustomInspector]
public abstract partial class Binder: Node
{
    private IBindingSource _bindingSource;
    protected IBindingSource BindingSource => _bindingSource;

    public override void _Ready()
    {
        base._Ready();
        
        if(Engine.IsEditorHint())
            return;

        for (var x = GetParentOrNull<Node>(); x != null; x = x.GetParentOrNull<Node>())
        {
            if (x is not IBindingSource source)
                continue;
            
            SetSource(source);
            break;
        }

        if (_bindingSource == null)
            GD.PrintErr("Binder: No IBindingSource found in parents.");
    }

    private void SetSource(IBindingSource source)
    {
        if (_bindingSource != null)
            _bindingSource.GetBindings?.UnregisterPropertyChange(OnBindingValueChanged);
        
        _bindingSource = source;
        if (_bindingSource != null)
        {
            OnBindingRestored(_bindingSource.GetBindings);
            _bindingSource.GetBindings?.RegisterPropertyChange(OnBindingValueChanged);
        }
    }

    public override void _ExitTree()
    {
        if (_bindingSource != null)
        {
            _bindingSource.GetBindings?.UnregisterPropertyChange(OnBindingValueChanged);
            _bindingSource = null;
        }
        base._ExitTree();
    }

    protected abstract void OnBindingRestored(IPropertyMap bindingMap);
    protected abstract void OnBindingValueChanged(string propertyName);

    /// <summary>
    /// Validates that the binder is configured against a valid binding source and key.
    /// Returns an empty string if valid or not yet configured; otherwise returns an error message.
    /// Override in derived classes to add source-type/key validation.
    /// </summary>
    public virtual string ValidateBinding() => string.Empty;
}
