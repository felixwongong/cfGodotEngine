using cfEngine.DataStructure;

namespace cfGodotEngine.Binding;

public interface IBindingSource
{
    public IPropertyMap GetBindings { get; }
}