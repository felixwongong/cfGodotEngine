using System;
using Godot;

namespace cfGodotEngine.Util;

[Tool]
[GlobalClass]
public partial class TypeRef : Resource
{
    [Export]
    private string typeName
    {
        get => type?.AssemblyQualifiedName ?? string.Empty;
        set => type = Type.GetType(value);
    }

    private Type type;
}