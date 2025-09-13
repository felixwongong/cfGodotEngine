using System.Runtime.CompilerServices;
using Godot;
using Vector3 = System.Numerics.Vector3;

namespace cfGodotEngine.Util;

public static class RandomUtil
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 RandVec3Range(this RandomNumberGenerator random, Vector3 start, Vector3 end)
    {
        var x = random.RandfRange(start.X, end.X);
        var y = random.RandfRange(start.Y, end.Y);
        var z = random.RandfRange(start.Z, end.Z);
        return new Vector3(x, y, z);
    }
    
    public static Vector2 RandVec2Range(this RandomNumberGenerator random, Vector2 start, Vector2 end)
    {
        var x = random.RandfRange(start.X, end.X);
        var y = random.RandfRange(start.Y, end.Y);
        return new Vector2(x, y);
    }

    public static Vector3I RandVec3IRange(this RandomNumberGenerator random, Vector3 start, Vector3 end)
    {
        var x = random.RandiRange((int)start.X, (int)end.X - 1);
        var y = random.RandiRange((int)start.Y, (int)end.Y - 1);
        var z = random.RandiRange((int)start.Z, (int)end.Z - 1);
        return new Vector3I(x, y, z);
    }
    
    public static Vector3I RandVec3IRange(this RandomNumberGenerator random, Vector3I start, Vector3I end)
    {
        var x = random.RandiRange(start.X, end.X - 1);
        var y = random.RandiRange(start.Y, end.Y - 1);
        var z = random.RandiRange(start.Z, end.Z - 1);
        return new Vector3I(x, y, z);
    }
    
    public static Vector2I RandVec2IRange(this RandomNumberGenerator random, Vector2 start, Vector2 end)
    {
        var x = random.RandiRange((int)start.X, (int)end.X - 1);
        var y = random.RandiRange((int)start.Y, (int)end.Y - 1);
        return new Vector2I(x, y);
    }
    
    public static Vector2I RandVec2IRange(this RandomNumberGenerator random, Vector2I start, Vector2I end)
    {
        var x = random.RandiRange(start.X, end.X - 1);
        var y = random.RandiRange(start.Y, end.Y - 1);
        return new Vector2I(x, y);
    }
}