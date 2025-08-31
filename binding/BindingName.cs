using Godot;

namespace cfGodotEngine.Util;

[Tool]
[GlobalClass]
public partial class BindingName : Resource
{
    [Export]
    public string typeName;
    [Export]
    public string propertyName;
}