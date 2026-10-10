using Godot;

namespace cfGodotEngine.UI;

/// <summary>How <see cref="SafeRect"/> keeps content inside the device safe area.</summary>
public enum SafeMode
{
    /// <summary>Inset self via anchors/offsets; leave children at design sizes (default).</summary>
    Inset = 0,

    /// <summary>Keep self full-rect and scale children to fit the usable area.</summary>
    ScaleToFit = 1,

    /// <summary>Inset self, then scale children down only when the safe rect is smaller.</summary>
    Hybrid = 2,
}

/// <summary>
/// Pure safe-area computation shared with <see cref="SafeRect"/>. Uses only
/// value structs (<see cref="Vector2"/>, <see cref="Vector2I"/>,
/// <see cref="Rect2I"/>, floats) so it is directly unit-testable under plain
/// NUnit, where Godot nodes cannot be instantiated.
/// </summary>
public static class SafeRectMath
{
    /// <summary>Per-edge safe-area insets in design units.</summary>
    public readonly struct Insets
    {
        public readonly float Top;
        public readonly float Bottom;
        public readonly float Left;
        public readonly float Right;

        public Insets(float top, float bottom, float left, float right)
        {
            Top = top;
            Bottom = bottom;
            Left = left;
            Right = right;
        }

        /// <summary>Build from left/top/right/bottom order.</summary>
        public static Insets FromLtrb(float left, float top, float right, float bottom)
            => new Insets(top, bottom, left, right);

        public static readonly Insets Zero = new Insets(0f, 0f, 0f, 0f);

        /// <summary>True when all four edges are exactly zero.</summary>
        public bool IsZero => Top == 0f && Bottom == 0f && Left == 0f && Right == 0f;
    }

    /// <summary>Signed values for a Control's <c>Offset*</c> properties.</summary>
    public readonly struct RectOffsets
    {
        public readonly float Left;
        public readonly float Top;
        public readonly float Right;
        public readonly float Bottom;

        // Non-public: constructed only by SafeRectMath's own computed plans.
        internal RectOffsets(float left, float top, float right, float bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public static readonly RectOffsets Zero = new RectOffsets(0f, 0f, 0f, 0f);
    }

    /// <summary>The computed layout a <see cref="SafeRect"/> applies to itself and its children.</summary>
    public readonly struct LayoutPlan
    {
        /// <summary>Applied to the SafeRect's own anchors/offsets.</summary>
        public readonly RectOffsets SelfOffsets;

        /// <summary>Scale applied to direct Control children (1 = no scaling).</summary>
        public readonly float ChildScale;

        // Non-public: built only via LayoutPlan.For.
        internal LayoutPlan(RectOffsets selfOffsets, float childScale)
        {
            SelfOffsets = selfOffsets;
            ChildScale = childScale;
        }

        public static LayoutPlan For(SafeMode mode, Vector2 parentSize, Insets masked)
        {
            switch (mode)
            {
                case SafeMode.ScaleToFit:
                    return new LayoutPlan(RectOffsets.Zero, ChildScale(parentSize, masked));

                case SafeMode.Hybrid:
                    return new LayoutPlan(
                        InsetOffsets(masked),
                        masked.IsZero ? 1f : ChildScale(parentSize, masked));

                case SafeMode.Inset:
                default:
                    return new LayoutPlan(InsetOffsets(masked), 1f);
            }
        }

        private static RectOffsets InsetOffsets(Insets masked)
            => new RectOffsets(+masked.Left, +masked.Top, -masked.Right, -masked.Bottom);
    }

    /// <summary>
    /// Convert a screen-pixel safe area into design-unit edge insets. A
    /// non-positive <paramref name="scale"/> is treated as 1; each edge is
    /// clamped to <c>&gt;= 0</c> (e.g. when the window is larger than the screen).
    /// </summary>
    public static Insets FromScreen(Rect2I safeArea, Vector2I screenSize, float scale)
    {
        float f = scale <= 0f ? 1f : scale;

        float left = safeArea.Position.X / f;
        float top = safeArea.Position.Y / f;
        float right = (screenSize.X - (safeArea.Position.X + safeArea.Size.X)) / f;
        float bottom = (screenSize.Y - (safeArea.Position.Y + safeArea.Size.Y)) / f;

        return new Insets(
            Mathf.Max(0f, top),
            Mathf.Max(0f, bottom),
            Mathf.Max(0f, left),
            Mathf.Max(0f, right));
    }

    /// <summary>Zero out each disabled edge (toggle == false becomes 0).</summary>
    public static Insets Mask(Insets insets,
        bool applyTop, bool applyBottom, bool applyLeft, bool applyRight)
        => new Insets(
            applyTop ? insets.Top : 0f,
            applyBottom ? insets.Bottom : 0f,
            applyLeft ? insets.Left : 0f,
            applyRight ? insets.Right : 0f);

    /// <summary>Parent size minus the horizontal/vertical insets, each axis clamped &gt;= 0.</summary>
    public static Vector2 UsableSize(Vector2 parentSize, Insets insets)
        => new Vector2(
            Mathf.Max(0f, parentSize.X - insets.Left - insets.Right),
            Mathf.Max(0f, parentSize.Y - insets.Top - insets.Bottom));

    /// <summary>
    /// Fit scale for children: the smaller of the two usable/parent ratios,
    /// clamped to <c>[0, 1]</c> so children never upscale. Returns 1 when either
    /// parent axis is non-positive.
    /// </summary>
    public static float ChildScale(Vector2 parentSize, Insets insets)
    {
        if (parentSize.X <= 0f || parentSize.Y <= 0f) return 1f;

        Vector2 usable = UsableSize(parentSize, insets);
        return Mathf.Clamp(
            Mathf.Min(usable.X / parentSize.X, usable.Y / parentSize.Y),
            0f,
            1f);
    }
}
