using System.Runtime.CompilerServices;
using Godot;
using Vector3 = System.Numerics.Vector3;

namespace cfGodotEngine.Util;

public static class RandomUtil
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 RandVecRange(this RandomNumberGenerator random, Vector3 start, Vector3 end)
    {
        var x = random.RandfRange(start.X, end.X);
        var y = random.RandfRange(start.Y, end.Y);
        var z = random.RandfRange(start.Z, end.Z);
        return new Vector3(x, y, z);
    }
}