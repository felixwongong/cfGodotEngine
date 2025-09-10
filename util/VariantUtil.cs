using System.Runtime.CompilerServices;
using Godot;

namespace cfGodotEngine.Util;

public static class VariantUtil
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPrimitive(this Variant variant)
    {
        return 
            variant.VariantType is Variant.Type.Bool or
            Variant.Type.Int or
            Variant.Type.Float;
    }
}