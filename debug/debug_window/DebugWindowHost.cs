using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;

namespace cfGodotEngine.Debug;

/// <summary>
/// Always-compiled host that renders the windows requested through <see cref="DebugWindowRegistry"/>.
/// One CanvasLayer (Layer 126, below the command console at 128) owns every card as an ordinary
/// Control child. The registry is consulted only through an event handler that enqueues requests;
/// all scene mutation (factory invocation, <c>AddChild</c>, stacking, drag, resize, close) happens
/// in Godot main-thread callbacks (<c>_Ready</c> / <c>_Process</c> / input / viewport resize).
/// </summary>
public sealed partial class DebugWindowHost : CanvasLayer
{
    private const int HostLayer = 126;
    private const float CascadeOffsetPx = 32f;
    private const float DesiredWidthRatio = 0.92f;
    private const float DesiredHeightRatio = 0.65f;
    private const float MaxCardWidth = 1600f;
    private const float MaxCardHeight = 2000f;
    private const float DragThresholdPx = 8f;
    private const int TitleFontSize = 28;

    /// <summary>Optional content metadata a card's own subtree may publish for a live title.</summary>
    private const string CardTitleMeta = "info_debug_window_title";

    private readonly ConcurrentQueue<OpenRequest> _pending = new();
    private readonly Dictionary<string, WindowCard> _windows = new(StringComparer.Ordinal);

    private Viewport _viewport;
    private Vector2 _lastCardPosition;
    private bool _hasCascadeOrigin;

    public override void _Ready()
    {
        DebugUiScale.EnsureRegistered();
        Layer = HostLayer;
        _viewport = GetViewport();
        DebugWindowRegistry.WindowOpenRequested += OnWindowOpenRequested;
        if (_viewport != null)
            _viewport.SizeChanged += OnViewportResized;
        SetProcess(true);
    }

    public override void _ExitTree()
    {
        DebugWindowRegistry.WindowOpenRequested -= OnWindowOpenRequested;
        if (_viewport != null)
        {
            _viewport.SizeChanged -= OnViewportResized;
            _viewport = null;
        }

        // Discard anything that never reached a main-thread drain.
        while (_pending.TryDequeue(out _))
        {
        }
    }

    public override void _Process(double delta)
    {
        while (_pending.TryDequeue(out var request))
            HandleOpenRequest(request);

        // Optional live title: a card whose content publishes "info_debug_window_title" metadata
        // updates its own title label only, without the host knowing anything about that window.
        foreach (var card in _windows.Values)
            RefreshCardTitle(card);
    }

    private static void RefreshCardTitle(WindowCard card)
    {
        if (card.Content == null || card.TitleLabel == null)
            return;

        var title = card.BaseTitle;
        if (card.Content.HasMeta(CardTitleMeta))
            title = card.Content.GetMeta(CardTitleMeta).AsString();

        if (string.IsNullOrWhiteSpace(title) || string.Equals(title, card.CurrentTitle, StringComparison.Ordinal))
            return;

        card.TitleLabel.Text = title;
        card.CurrentTitle = title;
    }

    // Off-thread safe: managed enqueue only, no Godot access.
    private void OnWindowOpenRequested(string name, string title, Func<Control> factory)
    {
        _pending.Enqueue(new OpenRequest(name, title, factory));
    }

    private void HandleOpenRequest(OpenRequest request)
    {
        if (_windows.TryGetValue(request.Name, out var existing))
        {
            // Reopen: bring the existing card forward without rebuilding it.
            BringToFront(existing);
            return;
        }

        var card = new WindowCard(request.Name);
        if (!BuildCard(card, request))
            return;

        AddChild(card.Panel);
        _windows[request.Name] = card;
        PositionCard(card);
        Callable.From(ClampAllCards).CallDeferred();
    }

    private bool BuildCard(WindowCard card, OpenRequest request)
    {
        Control content;
        try
        {
            content = request.Factory?.Invoke();
        }
        catch (Exception ex)
        {
            GD.PushError($"[DebugWindowHost] Factory for '{request.Name}' threw: {ex.Message}");
            return false;
        }

        if (content == null)
        {
            GD.PushWarning($"[DebugWindowHost] Factory for '{request.Name}' returned null.");
            return false;
        }

        if (content.GetParent() != null)
        {
            GD.PushWarning($"[DebugWindowHost] Factory for '{request.Name}' returned an already-parented Control.");
            return false;
        }

        var panel = new PanelContainer();
        card.Panel = panel;
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.9f),
            ContentMarginLeft = DebugUiScale.S(12f),
            ContentMarginTop = DebugUiScale.S(8f),
            ContentMarginRight = DebugUiScale.S(12f),
            ContentMarginBottom = DebugUiScale.S(8f)
        });

        var layout = new VBoxContainer();
        DebugUiScale.ScaleInheritedConstant(layout, "separation");
        panel.AddChild(layout);

        var header = new HBoxContainer();
        DebugUiScale.ScaleInheritedConstant(header, "separation");
        var title = new Label
        {
            Text = request.Title,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        title.AddThemeFontSizeOverride("font_size", DebugUiScale.S(TitleFontSize));
        header.AddChild(title);
        card.BaseTitle = request.Title;
        card.CurrentTitle = request.Title;
        card.TitleLabel = title;
        card.Content = content;

        var dragArea = new Control
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        header.AddChild(dragArea);

        var close = new Button
        {
            Text = "✕",
            FocusMode = Control.FocusModeEnum.None
        };
        // The desktop close button deliberately has no explicit minimum (unchanged at ×1). On a
        // ×2 device it gets the discussion's ≥120×120 touch target plus its scaled inherited font.
        if (DebugUiScale.Factor > 1)
        {
            close.CustomMinimumSize = new Vector2(120f, 120f);
            DebugUiScale.ScaleInheritedFont(close);
        }

        close.Pressed += () => CloseWindow(request.Name);
        header.AddChild(close);
        layout.AddChild(header);

        header.GuiInput += @event => OnHeaderGuiInput(card, @event);

        var scroll = new ScrollContainer
        {
            ClipContents = true,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        content.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.AddChild(content);
        layout.AddChild(scroll);

        return true;
    }

    private void OnHeaderGuiInput(WindowCard card, InputEvent @event)
    {
        // Mouse-driven; touch arrives through the engine's emulate_mouse_from_touch (default true).
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            if (mb.Pressed)
            {
                card.Pressed = true;
                card.Dragging = false;
                card.DragDistance = 0f;
                BringToFront(card);
            }
            else
            {
                card.Pressed = false;
                card.Dragging = false;
            }
        }
        else if (@event is InputEventMouseMotion mm && card.Pressed)
        {
            card.DragDistance += mm.Relative.Length();
            if (card.DragDistance >= DebugUiScale.S(DragThresholdPx))
                card.Dragging = true;

            if (card.Dragging)
                MoveCard(card, card.Panel.Position + mm.Relative);
        }
    }

    private void BringToFront(WindowCard card)
    {
        if (card.Panel.GetParent() != this)
            return;

        MoveChild(card.Panel, GetChildCount() - 1);
    }

    private void CloseWindow(string name)
    {
        if (!_windows.TryGetValue(name, out var card))
            return;

        _windows.Remove(name);
        card.Pressed = false;
        card.Dragging = false;
        card.DragDistance = 0f;
        card.Panel.Visible = false;
        card.Panel.QueueFree();
    }

    private void PositionCard(WindowCard card)
    {
        var bounds = GetVisibleBounds();
        card.Panel.Size = DesiredSize(bounds);

        // First card is centered; each subsequent distinct card cascades 32px from the previous
        // card's placed position, then clamps into the visible rect.
        var pos = _hasCascadeOrigin
            ? _lastCardPosition + new Vector2(DebugUiScale.S(CascadeOffsetPx), DebugUiScale.S(CascadeOffsetPx))
            : (bounds - card.Panel.Size) * 0.5f;

        card.Panel.Position = ClampToBounds(pos, card.Panel.Size, bounds);
        _lastCardPosition = card.Panel.Position;
        _hasCascadeOrigin = true;
    }

    private void MoveCard(WindowCard card, Vector2 pos)
    {
        card.Panel.Position = ClampToBounds(pos, card.Panel.Size, GetVisibleBounds());
    }

    private void OnViewportResized()
    {
        var bounds = GetVisibleBounds();
        foreach (var card in _windows.Values)
            card.Panel.Size = DesiredSize(bounds);

        // Clamp after the containers have re-laid-out with their enforced minimum sizes.
        Callable.From(ClampAllCards).CallDeferred();
    }

    private void ClampAllCards()
    {
        var bounds = GetVisibleBounds();
        foreach (var card in _windows.Values)
            card.Panel.Position = ClampToBounds(card.Panel.Position, card.Panel.Size, bounds);
    }

    private static Vector2 ClampToBounds(Vector2 pos, Vector2 size, Vector2 bounds)
    {
        pos.X = Mathf.Clamp(pos.X, 0f, Mathf.Max(0f, bounds.X - size.X));
        pos.Y = Mathf.Clamp(pos.Y, 0f, Mathf.Max(0f, bounds.Y - size.Y));
        return pos;
    }

    private static Vector2 DesiredSize(Vector2 bounds)
    {
        var width = Mathf.Min(bounds.X * DesiredWidthRatio, DebugUiScale.S(MaxCardWidth));
        var height = Mathf.Min(bounds.Y * DesiredHeightRatio, DebugUiScale.S(MaxCardHeight));
        return new Vector2(Mathf.Max(0f, width), Mathf.Max(0f, height));
    }

    private Vector2 GetVisibleBounds()
    {
        var viewport = _viewport ?? GetViewport();
        return viewport?.GetVisibleRect().Size ?? Vector2.Zero;
    }

    private readonly struct OpenRequest
    {
        public OpenRequest(string name, string title, Func<Control> factory)
        {
            Name = name;
            Title = title;
            Factory = factory;
        }

        public string Name { get; }

        public string Title { get; }

        public Func<Control> Factory { get; }
    }

    private sealed class WindowCard
    {
        public WindowCard(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public PanelContainer Panel { get; set; }

        public Control Content { get; set; }

        public Label TitleLabel { get; set; }

        public string BaseTitle { get; set; } = string.Empty;

        public string CurrentTitle { get; set; } = string.Empty;

        public bool Pressed { get; set; }

        public bool Dragging { get; set; }

        public float DragDistance { get; set; }
    }
}
