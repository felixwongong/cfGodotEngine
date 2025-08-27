using cfEngine.DataStructure;

namespace cfGodotEngine.Binding;

public interface IPropertySource
{
    public IPropertyMap GetProperties { get; }
}