using System.Collections.Generic;
using Godot;

namespace cfGodotEngine.Info;

public abstract partial class ResourceInfo<TInfo>: Resource
{
    public abstract IEnumerable<TInfo> GetInfos { get; }
}