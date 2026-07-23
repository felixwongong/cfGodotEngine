using System;
using System.Collections.Generic;
using System.Linq;

namespace cfGodotEngine.Binding;

public static partial class BindingSourceRegistryHost
{
    private static readonly Dictionary<string, IReadOnlyList<string>> _byTypeName = new();

    public static void Register(string typeName, IEnumerable<string> keys) =>
        _byTypeName[typeName] = keys.ToArray();

    public static IReadOnlyCollection<string> TypeNames => _byTypeName.Keys;

    public static IReadOnlyList<string> GetKeys(string typeName) =>
        _byTypeName.TryGetValue(typeName, out var k) ? k : Array.Empty<string>();
}
