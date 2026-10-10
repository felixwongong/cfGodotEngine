#if TOOLS
using Godot;

namespace cfGodotEngine.SceneManagement;

public abstract partial class Actor2D
{
    private static readonly Color SelectionColor = new(1f, 0.55f, 0f);
    [Export] private bool enableDebugDraw = false;
    private bool _editorSelected;

    public override void _EnterTree()
    {
        if (Engine.IsEditorHint())
            EditorInterface.Singleton.GetSelection().SelectionChanged += OnEditorSelectionChanged;
    }

    public override void _ExitTree()
    {
        if (Engine.IsEditorHint())
            EditorInterface.Singleton.GetSelection().SelectionChanged -= OnEditorSelectionChanged;
    }

    private void OnEditorSelectionChanged()
    {
        if (!enableDebugDraw || !IsInsideTree())
            return;

        bool selected = false;
        foreach (var node in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (node == this)
            {
                selected = true;
                break;
            }
        }

        if (selected != _editorSelected)
        {
            _editorSelected = selected;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!Engine.IsEditorHint() || !_editorSelected)
            return;

        var bounds = GetEditorVisualBounds();
        DrawRect(bounds, SelectionColor, filled: false, width: 2f);

        DrawLine(bounds.GetCenter() + new Vector2(-8, 0), bounds.GetCenter() + new Vector2(8, 0), SelectionColor, 1.5f);
        DrawLine(bounds.GetCenter() + new Vector2(0, -8), bounds.GetCenter() + new Vector2(0, 8), SelectionColor, 1.5f);

        var font = ThemeDB.FallbackFont;
        var label = $"{Name} ({GetType().Name})";
        var labelPos = new Vector2(bounds.Position.X, bounds.Position.Y - 6);
        DrawString(font, labelPos + new Vector2(1, 1), label, fontSize: 14, modulate: new Color(0, 0, 0, 0.8f));
        DrawString(font, labelPos, label, fontSize: 14, modulate: SelectionColor);
    }

    private Rect2 GetEditorVisualBounds()
    {
        var rect = new Rect2(new Vector2(-16, -16), new Vector2(32, 32));
        var found = false;

        foreach (var child in GetChildren())
        {
            Rect2? childRect = child switch
            {
                Control c => new Rect2(c.Position, c.Size),
                Sprite2D s when s.Texture != null => new Rect2(s.Position - s.Texture.GetSize() * 0.5f, s.Texture.GetSize()),
                CollisionShape2D cs when cs.Shape != null => new Rect2(cs.Position - cs.Shape.GetRect().Size * 0.5f, cs.Shape.GetRect().Size),
                _ => null,
            };

            if (childRect is Rect2 r)
            {
                rect = found ? rect.Merge(r) : r;
                found = true;
            }
        }

        return rect.Grow(4);
    }
}
#endif
