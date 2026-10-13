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

    /// <summary>
    /// Phone-class mode, resolved once (and only once) in <see cref="_Ready"/> on the main thread.
    /// Density already has rebuild-time preview semantics, so a live mode switch is deliberately not
    /// supported: opening a window in a different mode requires a fresh host (restart the UI).
    /// </summary>
    private bool _isPhone;

    private Func<Rect2> _safeAreaReader = DefaultSafeAreaReader;
    private bool _hasPhonePlacementRect;
    private Rect2 _phonePlacementRect;

    /// <summary>
    /// Test seam: replaces the native display-safe-area read. Defaults to
    /// <see cref="DisplayServer.GetDisplaySafeArea"/> wrapped as a <see cref="Rect2"/>; replaceable
    /// by the compiled probe only (never a project setting or game-visible override). Main-thread
    /// only. The host never inspects this delegate's origin — synthetic coordinates go through the
    /// same conversion and placement path as native ones.
    /// </summary>
    internal Func<Rect2> SafeAreaReader
    {
        get => _safeAreaReader;
        set => _safeAreaReader = value ?? DefaultSafeAreaReader;
    }

    public override void _Ready()
    {
        DebugUiScale.EnsureRegistered();
        Layer = HostLayer;
        _viewport = GetViewport();
        _isPhone = DebugUiScale.IsPhoneMode;
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

        // Phone windows track the native safe rectangle every frame (main thread). This covers a
        // safe-area change that never emits a viewport SizeChanged event (e.g. an inset appearing
        // while the window is already full-screen). Placement is only written when the mapped
        // rectangle actually changes, so settled cards are not re-laid-out needlessly.
        if (_isPhone)
            ReconcilePhoneSafeArea();

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
        if (_isPhone)
            ReconcilePhoneStack();
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

        // Phone-only chrome: the fullscreen card is the input boundary for its rectangle, so the
        // panel/layout/header/scroll chain explicitly keeps the blocking filter (Control's default
        // is STOP). The title label and drag spacer stay Ignore and the close control stays a real
        // Button, so the header still routes its own clicks and content still scrolls. The desktop
        // tree is deliberately left untouched (no explicit filters, no full-viewport STOP wrapper).
        if (_isPhone)
        {
            panel.MouseFilter = Control.MouseFilterEnum.Stop;
            layout.MouseFilter = Control.MouseFilterEnum.Stop;
            header.MouseFilter = Control.MouseFilterEnum.Stop;
            scroll.MouseFilter = Control.MouseFilterEnum.Stop;
        }

        return true;
    }

    private void OnHeaderGuiInput(WindowCard card, InputEvent @event)
    {
        // Phone windows never float: refusing motion here (before any mouse/touch handling) means
        // neither the header drag nor an emulated-mouse tap on the header can move or reorder the
        // fullscreen card. The close button keeps its own Pressed hookup and content keeps its
        // ScrollContainer, so this returns before any branch that would reinterpret the event.
        if (_isPhone)
        {
            card.Pressed = false;
            card.Dragging = false;
            card.DragDistance = 0f;
            return;
        }

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
        if (_isPhone)
            ReconcilePhoneStack();
    }

    private void CloseWindow(string name)
    {
        if (!_windows.TryGetValue(name, out var card))
            return;

        // Remove from the dictionary first, so a deferred QueueFree cannot leave the closing card
        // selectable as the top of the stack, and hide it so nothing renders during that frame.
        _windows.Remove(name);
        card.Pressed = false;
        card.Dragging = false;
        card.DragDistance = 0f;
        card.Panel.Visible = false;
        card.Panel.QueueFree();
        if (_isPhone)
            ReconcilePhoneStack();
    }

    /// <summary>
    /// Phone stack reconciliation: sibling order <em>is</em> the stack (no second store). Exactly the
    /// last still-owned card in sibling order is visible; every lower card stays in the tree with its
    /// content, state and scroll position intact but hidden and input-inactive. Closing a non-top
    /// card therefore cannot disturb the top, and closing the top reveals its predecessor.
    /// </summary>
    private void ReconcilePhoneStack()
    {
        if (!_isPhone)
            return;

        WindowCard top = null;
        var topIndex = int.MinValue;
        foreach (var card in _windows.Values)
        {
            card.Pressed = false;
            card.Dragging = false;
            card.DragDistance = 0f;

            if (card.Panel.GetParent() != this)
                continue;

            var index = card.Panel.GetIndex();
            if (index >= topIndex)
            {
                topIndex = index;
                top = card;
            }
        }

        foreach (var card in _windows.Values)
            card.Panel.Visible = card == top;
    }

    private void PositionCard(WindowCard card)
    {
        if (_isPhone)
        {
            var rect = ResolvePhonePlacementRect();
            _hasPhonePlacementRect = true;
            _phonePlacementRect = rect;
            card.Panel.Position = rect.Position;
            card.Panel.Size = rect.Size;
            return;
        }

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
        if (_isPhone)
        {
            card.Pressed = false;
            card.Dragging = false;
            card.DragDistance = 0f;
            return;
        }

        card.Panel.Position = ClampToBounds(pos, card.Panel.Size, GetVisibleBounds());
    }

    private void OnViewportResized()
    {
        if (_isPhone)
        {
            // The stretch/layout update that follows a resize changes the final transform, so the
            // recomputation is deferred and forced (the previous rectangle may coincidentally match).
            Callable.From(ReapplyPhonePlacement).CallDeferred();
            return;
        }

        var bounds = GetVisibleBounds();
        foreach (var card in _windows.Values)
            card.Panel.Size = DesiredSize(bounds);

        // Clamp after the containers have re-laid-out with their enforced minimum sizes.
        Callable.From(ClampAllCards).CallDeferred();
    }

    private void ClampAllCards()
    {
        if (_isPhone)
        {
            ReapplyPhonePlacement();
            return;
        }

        var bounds = GetVisibleBounds();
        foreach (var card in _windows.Values)
            card.Panel.Position = ClampToBounds(card.Panel.Position, card.Panel.Size, bounds);
    }

    // ---- phone placement -------------------------------------------------

    private static Rect2 DefaultSafeAreaReader()
    {
        var rect = DisplayServer.GetDisplaySafeArea();
        return new Rect2(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
    }

    /// <summary>
    /// Maps a native (display-space) safe-area rectangle into host — i.e. card-parent — logical
    /// coordinates. Pure: no native reads, no scaling by <see cref="DebugUiScale.Factor"/>, no
    /// window-origin subtraction of its own and no camera transform.
    ///
    /// <para><paramref name="hostToScreen"/> is the panel-independent host→screen transform
    /// (<c>panel.GetScreenTransform() * panel.GetTransform().AffineInverse()</c>), which already
    /// folds in window position, stretch and orientation; its affine inverse therefore maps the
    /// display rectangle straight to host coordinates. All four corners are transformed and the
    /// min/max rectangle is intersected with the visible host rect. An empty/nonpositive/non-finite
    /// safe rect, a non-finite or singular transform, a non-positive visible rect or an empty
    /// intersection yields the full visible host rect (the documented fallback), never a partial or
    /// inverted inset. Arbitrarily rotated/skewed host layers are not claimed to be supported.</para>
    /// </summary>
    internal static Rect2 MapDisplaySafeAreaToHost(Rect2 safeDisplay, Rect2 visibleHost, Transform2D hostToScreen)
    {
        if (!visibleHost.IsFinite() || visibleHost.Size.X <= 0f || visibleHost.Size.Y <= 0f)
            return visibleHost;

        if (!safeDisplay.IsFinite() || safeDisplay.Size.X <= 0f || safeDisplay.Size.Y <= 0f)
            return visibleHost;

        if (!hostToScreen.Origin.IsFinite() || !hostToScreen.X.IsFinite() || !hostToScreen.Y.IsFinite())
            return visibleHost;

        if (Mathf.Abs(hostToScreen.Determinant()) < 1e-6f)
            return visibleHost;

        var screenToHost = hostToScreen.AffineInverse();
        var cornerA = screenToHost * safeDisplay.Position;
        var cornerB = screenToHost * safeDisplay.End;
        var minX = Mathf.Min(cornerA.X, cornerB.X);
        var minY = Mathf.Min(cornerA.Y, cornerB.Y);
        var maxX = Mathf.Max(cornerA.X, cornerB.X);
        var maxY = Mathf.Max(cornerA.Y, cornerB.Y);

        var mapped = new Rect2(minX, minY, maxX - minX, maxY - minY);
        var intersection = mapped.Intersection(visibleHost);
        if (!intersection.IsFinite() || intersection.Size.X <= 0f || intersection.Size.Y <= 0f)
            return visibleHost;

        return intersection;
    }

    /// <summary>
    /// The live card whose panel supplies the host→screen transform: the top of the phone stack when
    /// one exists (any attached card works, because the panel's own translation is cancelled).
    /// </summary>
    private Control TopLivePanel()
    {
        Control best = null;
        var bestIndex = int.MinValue;
        foreach (var card in _windows.Values)
        {
            var panel = card.Panel;
            if (panel == null || panel.GetParent() != this)
                continue;

            var index = panel.GetIndex();
            if (index >= bestIndex)
            {
                bestIndex = index;
                best = panel;
            }
        }

        return best;
    }

    private Rect2 GetVisibleRect()
    {
        var viewport = _viewport ?? GetViewport();
        return viewport?.GetVisibleRect() ?? new Rect2();
    }

    /// <summary>
    /// Resolves the phone placement rectangle through the production mapper. Returns the full visible
    /// rect when no panel is attached (nothing to derive a transform from) or when the mapper falls
    /// back, so a phone card is always placed on a usable, non-empty rectangle.
    /// </summary>
    private Rect2 ResolvePhonePlacementRect()
    {
        var visible = GetVisibleRect();
        var panel = TopLivePanel();
        if (panel == null)
            return visible;

        var hostToScreen = panel.GetScreenTransform() * panel.GetTransform().AffineInverse();
        return MapDisplaySafeAreaToHost(ReadDisplaySafeArea(), visible, hostToScreen);
    }

    private Rect2 ReadDisplaySafeArea()
    {
        var reader = _safeAreaReader ?? DefaultSafeAreaReader;
        return reader();
    }

    /// <summary>Recomputes and re-applies phone placement (deferred after a resize/layout update).</summary>
    private void ReapplyPhonePlacement()
    {
        if (!_isPhone)
            return;

        _hasPhonePlacementRect = false;
        ReconcilePhoneSafeArea();
    }

    /// <summary>
    /// Applied from <see cref="_Process"/>: writes the newer placement to every live phone card only
    /// when the mapped rectangle changed, so a settled stack is stable across frames.
    /// </summary>
    private void ReconcilePhoneSafeArea()
    {
        if (TopLivePanel() == null)
        {
            _hasPhonePlacementRect = false;
            return;
        }

        var rect = ResolvePhonePlacementRect();
        if (rect.Size.X <= 0f || rect.Size.Y <= 0f)
            return;

        if (_hasPhonePlacementRect && _phonePlacementRect.IsEqualApprox(rect))
            return;

        _hasPhonePlacementRect = true;
        _phonePlacementRect = rect;

        foreach (var card in _windows.Values)
        {
            card.Panel.Position = rect.Position;
            card.Panel.Size = rect.Size;
        }
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
